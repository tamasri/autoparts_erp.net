using Dapper;

namespace AutoPartsERP.Application.Features.Payments.AllocatePayment;

public sealed record AllocatePaymentCommand(
    Guid PaymentId,
    IReadOnlyCollection<AllocatePaymentLineRequest> Allocations,
    string? Notes,
    string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Payments.Allocate;
    public string AuditModule => "PAYMENTS";
}

public sealed class AllocatePaymentCommandValidator : AbstractValidator<AllocatePaymentCommand>
{
    public AllocatePaymentCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.Allocations).NotNull().Must(x => x.Count > 0).WithMessage("At least one allocation is required.");
        RuleForEach(x => x.Allocations).ChildRules(line =>
        {
            line.RuleFor(x => x.InvoiceId).NotEmpty();
            line.RuleFor(x => x.AllocatedSyp).GreaterThanOrEqualTo(0);
            line.RuleFor(x => x.AllocatedUsd).GreaterThanOrEqualTo(0);
            line.When(x => x.AllocatedSyp == 0 && x.AllocatedUsd == 0, () =>
            {
                line.RuleFor(x => x.InvoiceId).NotEmpty().WithMessage("Allocation must have a value.");
            });
        });
    }
}

public sealed class AllocatePaymentCommandHandler : IRequestHandler<AllocatePaymentCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public AllocatePaymentCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(AllocatePaymentCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var payment = await connection.QuerySingleOrDefaultAsync<(Guid Id, decimal UnallocatedSyp, decimal UnallocatedUsd)>(
            new CommandDefinition(
                """
                SELECT id AS Id, unallocated_syp AS UnallocatedSyp, unallocated_usd AS UnallocatedUsd
                FROM payments
                WHERE id = @PaymentId
                FOR UPDATE;
                """,
                new { request.PaymentId },
                transaction,
                cancellationToken: cancellationToken));

        if (payment.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Payment.NotFound", "Payment was not found."));
        }

        foreach (var allocation in request.Allocations)
        {
            var invoice = await connection.QuerySingleOrDefaultAsync<(Guid Id, decimal BalanceSyp, decimal BalanceUsd, string Status)>(
                new CommandDefinition(
                    """
                    SELECT id AS Id, balance_syp AS BalanceSyp, balance_usd AS BalanceUsd, status AS Status
                    FROM invoices
                    WHERE id = @InvoiceId
                    FOR UPDATE;
                    """,
                    new { allocation.InvoiceId },
                    transaction,
                    cancellationToken: cancellationToken));

            if (invoice.Id == Guid.Empty)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("Invoice.NotFound", "Invoice was not found."));
            }

            if (string.Equals(invoice.Status, "VOID", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(invoice.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("Invoice.InvalidState", "Cannot allocate payments to a void invoice."));
            }

            if (allocation.AllocatedSyp > payment.UnallocatedSyp || allocation.AllocatedUsd > payment.UnallocatedUsd)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("Payment.OverAllocation", "Payment allocation exceeds remaining balance."));
            }

            if (allocation.AllocatedSyp > invoice.BalanceSyp || allocation.AllocatedUsd > invoice.BalanceUsd)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("Invoice.BalanceExceeded", "Allocation exceeds invoice balance."));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO payment_allocations (
                    id, payment_id, invoice_id, allocated_syp, allocated_usd, allocation_date, notes, created_at, created_by)
                VALUES (@Id, @PaymentId, @InvoiceId, @AllocatedSyp, @AllocatedUsd, CURRENT_DATE, @Notes, now(), @CreatedBy);
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    PaymentId = request.PaymentId,
                    allocation.InvoiceId,
                    allocation.AllocatedSyp,
                    allocation.AllocatedUsd,
                    request.Notes,
                    CreatedBy = _currentUser.UserId
                },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE invoices
                SET paid_syp = paid_syp + @AllocatedSyp,
                    paid_usd = paid_usd + @AllocatedUsd,
                    updated_at = now(),
                    updated_by = @UpdatedBy
                WHERE id = @InvoiceId;
                """,
                new
                {
                    allocation.InvoiceId,
                    allocation.AllocatedSyp,
                    allocation.AllocatedUsd,
                    UpdatedBy = _currentUser.UserId
                },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE payments
                SET allocated_syp = allocated_syp + @AllocatedSyp,
                    allocated_usd = allocated_usd + @AllocatedUsd,
                    updated_at = now()
                WHERE id = @PaymentId;
                """,
                new
                {
                    PaymentId = request.PaymentId,
                    allocation.AllocatedSyp,
                    allocation.AllocatedUsd
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(request.PaymentId);
    }
}
