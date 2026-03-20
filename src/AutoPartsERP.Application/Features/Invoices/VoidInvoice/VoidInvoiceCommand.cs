using Dapper;

namespace AutoPartsERP.Application.Features.Invoices.VoidInvoice;

public sealed record VoidInvoiceCommand(
    Guid InvoiceId,
    DateOnly InvoiceDate,
    string Reason,
    string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest, IMakerCheckerRequest, IPeriodSensitiveRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.Void;
    public string AuditModule => "INVOICES";
    public bool RequiresApproval => true;
    public DateTimeOffset OperationDate => InvoiceDate.ToDateTime(TimeOnly.MinValue);
    public string Module => "INVOICES";
}

public sealed class VoidInvoiceCommandValidator : AbstractValidator<VoidInvoiceCommand>
{
    public VoidInvoiceCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(3);
    }
}

public sealed class VoidInvoiceCommandHandler : IRequestHandler<VoidInvoiceCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public VoidInvoiceCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(VoidInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var invoice = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(
                """
                SELECT id, customer_id AS CustomerId, invoice_date AS InvoiceDate, total_syp AS TotalSyp, total_usd AS TotalUsd, paid_syp AS PaidSyp, paid_usd AS PaidUsd, status AS Status
                FROM invoices
                WHERE id = @InvoiceId
                FOR UPDATE;
                """,
                new { request.InvoiceId },
                transaction,
                cancellationToken: cancellationToken));

        if (invoice is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Invoice.NotFound", "Invoice was not found."));
        }

        if (!string.Equals((string)invoice.Status, "POSTED", StringComparison.OrdinalIgnoreCase))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Invoice.InvalidState", "Only posted invoices can be voided."));
        }

        if ((decimal)invoice.PaidSyp > 0m || (decimal)invoice.PaidUsd > 0m)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Invoice.HasAllocations", "Cannot void an invoice that has payment allocations."));
        }

        var reversalId = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO invoices (
                id, invoice_type, status, customer_id, invoice_date, due_date, original_invoice_id,
                subtotal_syp, subtotal_usd, discount_amount_syp, discount_amount_usd,
                delivery_fee_syp, delivery_fee_usd, tax_amount_syp, tax_amount_usd, total_syp, total_usd,
                paid_syp, paid_usd, fx_rate_id, fx_rate_snapshot, reason_code, notes, posted_at, posted_by,
                created_at, created_by)
            SELECT
                @ReversalId, 'CREDIT_NOTE', 'POSTED', customer_id, invoice_date, due_date, id,
                -total_syp, -total_usd, 0, 0, 0, 0, 0, 0,
                -total_syp, -total_usd, 0, 0,
                fx_rate_id, fx_rate_snapshot, 'VOID', @Reason, now(), @PostedBy, now(), @CreatedBy
            FROM invoices
            WHERE id = @InvoiceId;
            """,
            new
            {
                request.InvoiceId,
                ReversalId = reversalId,
                request.Reason,
                PostedBy = _currentUser.UserId,
                CreatedBy = _currentUser.UserId
            },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE invoices
            SET status = 'VOID',
                voided_at = now(),
                voided_by = @VoidedBy,
                void_reason = @Reason,
                updated_at = now(),
                updated_by = @VoidedBy
            WHERE id = @InvoiceId;
            """,
            new { request.InvoiceId, Reason = request.Reason, VoidedBy = _currentUser.UserId },
            transaction,
            cancellationToken: cancellationToken));

        var outboxMessage = OutboxMessage.Create(
            OutboxEventTypes.InvoiceVoided,
            "Invoice",
            request.InvoiceId,
            new InvoiceVoidedPayload(
                request.InvoiceId,
                reversalId,
                request.Reason,
                _currentUser.UserId),
            _currentUser.CorrelationId);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO outbox_messages (
                id, event_type, aggregate_type, aggregate_id, payload_json, occurred_at,
                processed_at, processing_error, retry_count, correlation_id)
            VALUES (
                @Id, @EventType, @AggregateType, @AggregateId, @PayloadJson, @OccurredAt,
                @ProcessedAt, @ProcessingError, @RetryCount, @CorrelationId);
            """,
            new
            {
                outboxMessage.Id,
                outboxMessage.EventType,
                outboxMessage.AggregateType,
                outboxMessage.AggregateId,
                outboxMessage.PayloadJson,
                outboxMessage.OccurredAt,
                outboxMessage.ProcessedAt,
                outboxMessage.ProcessingError,
                outboxMessage.RetryCount,
                outboxMessage.CorrelationId
            },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(reversalId);
    }
}
