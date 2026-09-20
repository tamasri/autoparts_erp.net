using Dapper;

namespace AutoPartsERP.Application.Features.Purchasing;

// A supplier payment is recorded together with its allocation to one or more posted bills, so a payment never exists half-applied.

public sealed record CreateSupplierPaymentCommand(CreateSupplierPaymentRequest Request, string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest, IPeriodSensitiveRequest
{
    public string RequiredPermission => PermissionCodes.SupplierPayments.Create;
    public string AuditModule => "SUPPLIER_PAYMENTS";
    public DateTimeOffset OperationDate => Request.PaymentDate.ToDateTime(TimeOnly.MinValue);
    public string Module => "PURCHASES";
}

public sealed class CreateSupplierPaymentCommandValidator : AbstractValidator<CreateSupplierPaymentCommand>
{
    private static readonly string[] Methods = ["CASH", "BANK_TRANSFER", "CHEQUE", "USD_CASH"];

    public CreateSupplierPaymentCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Request.SupplierPartyId).NotEmpty();
        RuleFor(x => x.Request.AmountUsd).GreaterThan(0);
        RuleFor(x => x.Request.PaymentMethod).Must(m => Methods.Contains((m ?? string.Empty).Trim().ToUpperInvariant())).WithMessage("Unknown payment method.");
        RuleFor(x => x.Request.ChequeNumber).NotEmpty().When(x => string.Equals(x.Request.PaymentMethod, "CHEQUE", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Cheque number is required for cheque payments.");
        RuleFor(x => x.Request.Allocations).NotEmpty().WithMessage("Choose at least one bill to pay.");
        RuleForEach(x => x.Request.Allocations).ChildRules(a =>
        {
            a.RuleFor(l => l.PurchaseInvoiceId).NotEmpty();
            a.RuleFor(l => l.AmountUsd).GreaterThan(0);
        });
        RuleFor(x => x.Request).Must(r => r.Allocations.Sum(a => a.AmountUsd) <= r.AmountUsd)
            .WithMessage("The allocations exceed the payment amount.");
    }
}

public sealed class CreateSupplierPaymentCommandHandler : IRequestHandler<CreateSupplierPaymentCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreateSupplierPaymentCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(CreateSupplierPaymentCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var id = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO supplier_payments (
                id, payment_number, supplier_party_id, payment_date, payment_method, amount_usd, reference_number, bank_name, cheque_number, notes, created_by)
            VALUES (
                @id, 'SPAY-' || to_char(@PaymentDate, 'YYYY') || '-' || lpad(nextval('supplier_payment_seq')::text, 5, '0'),
                @SupplierPartyId, @PaymentDate, @method, @AmountUsd, @ReferenceNumber, @BankName, @ChequeNumber, @Notes, @By);
            """,
            new
            {
                id, request.SupplierPartyId, request.PaymentDate, method = request.PaymentMethod.Trim().ToUpperInvariant(), request.AmountUsd,
                request.ReferenceNumber, request.BankName, request.ChequeNumber, request.Notes, By = _currentUser.UserId
            },
            transaction, cancellationToken: cancellationToken));

        foreach (var allocation in request.Allocations)
        {
            var bill = await connection.QuerySingleOrDefaultAsync<(Guid Id, Guid SupplierPartyId, string Status, decimal Balance)>(new CommandDefinition(
                "SELECT id AS Id, supplier_party_id AS SupplierPartyId, status AS Status, balance_usd AS Balance FROM purchase_invoices WHERE id = @Id FOR UPDATE;",
                new { Id = allocation.PurchaseInvoiceId }, transaction, cancellationToken: cancellationToken));

            if (bill.Id == Guid.Empty || bill.SupplierPartyId != request.SupplierPartyId || bill.Status != "POSTED")
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("SupplierPayment.InvalidBill", "A chosen bill does not belong to this supplier or is not posted."));
            }

            if (allocation.AmountUsd > bill.Balance)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<Guid>.Failure(new Error("SupplierPayment.OverAllocated", "A payment is larger than what is still owed on the bill."));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO supplier_payment_allocations (supplier_payment_id, purchase_invoice_id, allocated_usd) VALUES (@id, @Bill, @Amount);",
                new { id, Bill = allocation.PurchaseInvoiceId, Amount = allocation.AmountUsd }, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE purchase_invoices SET paid_usd = paid_usd + @Amount, balance_usd = balance_usd - @Amount, updated_at = now() WHERE id = @Bill;",
                new { Bill = allocation.PurchaseInvoiceId, Amount = allocation.AmountUsd }, transaction, cancellationToken: cancellationToken));
        }

        await PurchasingOutbox.AddAsync(connection, transaction, OutboxEventTypes.SupplierPaymentCreated, "SupplierPayment", id,
            new SupplierPaymentEventPayload(id), _currentUser.CorrelationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(id);
    }
}

public sealed record ReverseSupplierPaymentCommand(Guid Id, string Reason)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.SupplierPayments.Reverse;
    public string AuditModule => "SUPPLIER_PAYMENTS";
}

public sealed class ReverseSupplierPaymentCommandValidator : AbstractValidator<ReverseSupplierPaymentCommand>
{
    public ReverseSupplierPaymentCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(5);
    }
}

public sealed class ReverseSupplierPaymentCommandHandler : IRequestHandler<ReverseSupplierPaymentCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IPeriodLockService _periodLock;

    public ReverseSupplierPaymentCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, IPeriodLockService periodLock)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _periodLock = periodLock;
    }

    public async Task<Result<Guid>> Handle(ReverseSupplierPaymentCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var payment = await connection.QuerySingleOrDefaultAsync<(Guid Id, bool IsReversed, DateOnly PaymentDate)>(new CommandDefinition(
            "SELECT id AS Id, is_reversed AS IsReversed, payment_date AS PaymentDate FROM supplier_payments WHERE id = @Id FOR UPDATE;",
            new { request.Id }, transaction, cancellationToken: cancellationToken));
        if (payment.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("SupplierPayment.NotFound", "Supplier payment was not found."));
        }

        if (payment.IsReversed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("SupplierPayment.AlreadyReversed", "The payment was already reversed."));
        }

        if (await _periodLock.IsLockedAsync(payment.PaymentDate.Year, payment.PaymentDate.Month, "PURCHASES", cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Guid>.Failure(new Error("Period.Locked", "The accounting period of this payment is locked."));
        }

        // What the payment settled is owed again. (Allocations stay as history; the bill totals are what change.)
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE purchase_invoices p
            SET paid_usd = p.paid_usd - a.total, balance_usd = p.balance_usd + a.total, updated_at = now()
            FROM (SELECT purchase_invoice_id, SUM(allocated_usd) AS total FROM supplier_payment_allocations WHERE supplier_payment_id = @Id GROUP BY purchase_invoice_id) a
            WHERE p.id = a.purchase_invoice_id AND p.status <> 'VOID';
            """,
            new { request.Id }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE supplier_payments SET is_reversed = TRUE, reverse_reason = @Reason, reversed_at = now(), reversed_by = @By WHERE id = @Id;",
            new { request.Id, request.Reason, By = _currentUser.UserId }, transaction, cancellationToken: cancellationToken));

        await PurchasingOutbox.AddAsync(connection, transaction, OutboxEventTypes.SupplierPaymentReversed, "SupplierPayment", request.Id,
            new SupplierPaymentEventPayload(request.Id), _currentUser.CorrelationId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<Guid>.Success(request.Id);
    }
}

public sealed record GetSupplierPaymentsQuery(int PageNumber, int PageSize, Guid? SupplierPartyId)
    : IRequest<Result<PagedResponse<SupplierPaymentDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.SupplierPayments.Read;
}

public sealed class GetSupplierPaymentsQueryHandler : IRequestHandler<GetSupplierPaymentsQuery, Result<PagedResponse<SupplierPaymentDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetSupplierPaymentsQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<SupplierPaymentDto>>> Handle(GetSupplierPaymentsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(request.PageNumber, 1);
        var size = Math.Clamp(request.PageSize, 1, 200);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<SupplierPaymentDto>(new CommandDefinition(
            """
            SELECT p.id AS Id, p.payment_number AS PaymentNumber, p.supplier_party_id AS SupplierPartyId,
                   COALESCE(NULLIF(pa.display_name_ar, ''), pa.display_name) AS SupplierName, p.payment_date AS PaymentDate,
                   p.payment_method AS PaymentMethod, p.amount_usd AS AmountUsd,
                   p.amount_usd - COALESCE((SELECT SUM(allocated_usd) FROM supplier_payment_allocations a WHERE a.supplier_payment_id = p.id), 0) AS UnallocatedUsd,
                   p.is_reversed AS IsReversed
            FROM supplier_payments p INNER JOIN parties pa ON pa.id = p.supplier_party_id
            WHERE @SupplierPartyId::uuid IS NULL OR p.supplier_party_id = @SupplierPartyId
            ORDER BY p.payment_date DESC, p.created_at DESC
            OFFSET @Offset LIMIT @Size;
            """,
            new { request.SupplierPartyId, Offset = (page - 1) * size, Size = size }, cancellationToken: cancellationToken))).ToArray();
        var total = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM supplier_payments WHERE @SupplierPartyId::uuid IS NULL OR supplier_party_id = @SupplierPartyId;",
            new { request.SupplierPartyId }, cancellationToken: cancellationToken));
        return Result<PagedResponse<SupplierPaymentDto>>.Success(new PagedResponse<SupplierPaymentDto>(rows, page, size, total));
    }
}
