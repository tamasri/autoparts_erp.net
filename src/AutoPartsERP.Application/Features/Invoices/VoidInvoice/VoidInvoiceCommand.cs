using AutoPartsERP.Application.Features.Inventory;
using Dapper;

namespace AutoPartsERP.Application.Features.Invoices.VoidInvoice;

// Voiding cancels the posted document in ERPNext on its own date, so the period lock is checked against the invoice date (InvoicePeriod).
public sealed record VoidInvoiceCommand(
    Guid InvoiceId,
    string Reason,
    string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.Void;
    public string AuditModule => "INVOICES";
    public bool RequiresApproval => true;
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
    private readonly IPeriodLockService _periodLock;

    public VoidInvoiceCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, IPeriodLockService periodLock)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _periodLock = periodLock;
    }

    public async Task<Result<Guid>> Handle(VoidInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var invoice = await connection.QuerySingleOrDefaultAsync<VoidRow>(
            new CommandDefinition(
                """
                SELECT id AS Id, invoice_type AS Type, customer_id AS CustomerId, invoice_date AS InvoiceDate, total_syp AS TotalSyp, total_usd AS TotalUsd, paid_syp AS PaidSyp, paid_usd AS PaidUsd, status AS Status,
                       original_invoice_id AS OriginalInvoiceId, credit_applied_syp AS CreditAppliedSyp, credit_applied_usd AS CreditAppliedUsd, fx_rate_snapshot AS FxRate
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

        if (!string.Equals(invoice.Status, "POSTED", StringComparison.OrdinalIgnoreCase))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Invoice.InvalidState", "Only posted invoices can be voided."));
        }

        if (invoice.PaidSyp > 0m || invoice.PaidUsd > 0m)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Invoice.HasAllocations", "Cannot void an invoice that has payment allocations."));
        }

        // A sale with live returns is voided only after them (ERPNext refuses to cancel an invoice that has live returns).
        var returns = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT string_agg(invoice_number, '، ') FROM invoices WHERE original_invoice_id = @InvoiceId AND invoice_type = 'RETURN' AND status <> 'VOID';",
            new { request.InvoiceId }, transaction, cancellationToken: cancellationToken));
        if (returns is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Invoice.HasReturns", $"Returns {returns} were made against this invoice; void them first."));
        }

        var open = await InvoicePeriod.EnsureOpenAsync(_periodLock, connection, transaction, request.InvoiceId, cancellationToken);
        if (open.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(open.Error);
        }

        // Undo the stock effect of the posting: a voided sale returns its goods, a voided customer return takes them back out.
        var voidLines = (await connection.QueryAsync<VoidLine>(new CommandDefinition(
            "SELECT sku_id AS SkuId, location_id AS LocationId, batch_id AS BatchId, quantity AS Quantity, cost_price_usd AS CostPriceUsd FROM invoice_lines WHERE invoice_id = @InvoiceId;",
            new { request.InvoiceId },
            transaction,
            cancellationToken: cancellationToken))).ToList();
        var wasReturn = string.Equals(invoice.Type, "RETURN", StringComparison.OrdinalIgnoreCase);
        foreach (var l in voidLines)
        {
            // The goods move at the cost they were sold at: a voided sale's come back into the average, a voided return's leave it again.
            if (wasReturn)
            {
                await InventoryCosting.BlendOutAsync(connection, transaction, l.SkuId, l.Quantity, l.CostPriceUsd, invoice.FxRate, _currentUser.UserId, cancellationToken);
            }
            else
            {
                await InventoryCosting.BlendInAsync(connection, transaction, l.SkuId, l.Quantity, l.CostPriceUsd, invoice.FxRate, _currentUser.UserId, cancellationToken);
            }

            var moved = await InvoiceStockMover.MoveAsync(
                connection, transaction, request.InvoiceId, l.SkuId, l.LocationId, l.BatchId, l.Quantity,
                wasReturn ? StockDirection.Out : StockDirection.In, _currentUser.UserId,
                $"Voided invoice {request.InvoiceId}", cancellationToken);
            if (moved.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(moved.Error);
            }
        }

        if (wasReturn && invoice.OriginalInvoiceId is { } saleId)
        {
            // The returned sale owes again what this return had taken off it (before the return itself becomes immutable as VOID).
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE invoices SET credit_applied_syp = credit_applied_syp + @CreditAppliedSyp, credit_applied_usd = credit_applied_usd + @CreditAppliedUsd, updated_at = now()
                WHERE id = @saleId;
                UPDATE invoices SET credit_applied_syp = 0, credit_applied_usd = 0 WHERE id = @InvoiceId;
                """,
                new { invoice.CreditAppliedSyp, invoice.CreditAppliedUsd, saleId, request.InvoiceId }, transaction, cancellationToken: cancellationToken));
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

    private sealed record VoidLine(Guid SkuId, Guid LocationId, Guid? BatchId, decimal Quantity, decimal CostPriceUsd);

    private sealed record VoidRow(
        Guid Id, string Type, Guid CustomerId, DateOnly InvoiceDate, decimal TotalSyp, decimal TotalUsd, decimal PaidSyp, decimal PaidUsd, string Status,
        Guid? OriginalInvoiceId, decimal CreditAppliedSyp, decimal CreditAppliedUsd, decimal FxRate);
}
