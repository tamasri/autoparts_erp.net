using Dapper;

namespace AutoPartsERP.Application.Features.Payments.CreatePayment;

public sealed record CreatePaymentCommand(
    string PaymentType,
    Guid CustomerId,
    DateOnly PaymentDate,
    string PaymentMethod,
    decimal AmountSyp,
    decimal AmountUsd,
    Guid FxRateId,
    string? ReferenceNumber,
    string? BankName,
    string? ChequeNumber,
    DateOnly? ChequeDate,
    string? Notes,
    string IdempotencyKey)
    : IRequest<Result<PaymentDto>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest, IPeriodSensitiveRequest
{
    public string RequiredPermission => PermissionCodes.Payments.Create;
    public string AuditModule => "PAYMENTS";
    public DateTimeOffset OperationDate => PaymentDate.ToDateTime(TimeOnly.MinValue);
    public string Module => "PAYMENTS";
}

public sealed class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.PaymentType).NotEmpty();
        RuleFor(x => x.PaymentMethod).NotEmpty();
        RuleFor(x => x.AmountSyp).GreaterThanOrEqualTo(0);
        RuleFor(x => x.AmountUsd).GreaterThanOrEqualTo(0);
        RuleFor(x => x).Must(x => x.AmountSyp > 0 || x.AmountUsd > 0).WithMessage("Payment amount must be greater than zero.");
        RuleFor(x => x).Must(x => !string.Equals(x.PaymentMethod, "CHEQUE", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(x.ChequeNumber))
            .WithMessage("Cheque number is required for cheque payments.");
    }
}

public sealed class CreatePaymentCommandHandler : IRequestHandler<CreatePaymentCommand, Result<PaymentDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreatePaymentCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<PaymentDto>> Handle(CreatePaymentCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var customer = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Name)>(
            new CommandDefinition(
                "SELECT id AS Id, name AS Name FROM customers WHERE id = @CustomerId;",
                new { request.CustomerId },
                transaction,
                cancellationToken: cancellationToken));
        if (customer.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<PaymentDto>.Failure(new Error("Customer.NotFound", "Customer was not found."));
        }

        var fxRate = await connection.QuerySingleOrDefaultAsync<(Guid Id, decimal MidRate)>(
            new CommandDefinition(
                "SELECT id AS Id, mid_rate AS MidRate FROM fx_rates WHERE id = @FxRateId;",
                new { request.FxRateId },
                transaction,
                cancellationToken: cancellationToken));
        if (fxRate.Id == Guid.Empty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<PaymentDto>.Failure(new Error("FxRate.NotFound", "FX rate was not found."));
        }

        var paymentId = Guid.NewGuid();
        var paymentType = request.PaymentType.Trim().ToUpperInvariant();
        var paymentMethod = request.PaymentMethod.Trim().ToUpperInvariant();

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO payments (
                id, payment_type, customer_id, payment_date, payment_method, amount_syp, amount_usd,
                allocated_syp, allocated_usd, fx_rate_id, reference_number, bank_name, cheque_number,
                cheque_date, notes, created_at, created_by, received_by)
            VALUES (
                @Id, @PaymentType, @CustomerId, @PaymentDate, @PaymentMethod, @AmountSyp, @AmountUsd,
                0, 0, @FxRateId, @ReferenceNumber, @BankName, @ChequeNumber, @ChequeDate, @Notes, now(), @CreatedBy, @ReceivedBy);
            """,
            new
            {
                Id = paymentId,
                PaymentType = paymentType,
                request.CustomerId,
                request.PaymentDate,
                PaymentMethod = paymentMethod,
                request.AmountSyp,
                request.AmountUsd,
                request.FxRateId,
                request.ReferenceNumber,
                request.BankName,
                request.ChequeNumber,
                request.ChequeDate,
                request.Notes,
                CreatedBy = _currentUser.UserId,
                ReceivedBy = _currentUser.UserId
            },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);

        return Result<PaymentDto>.Success(PaymentMappings.ToPaymentDto(
            paymentId,
            $"PAY-{DateTime.UtcNow:yyyy}-{paymentId.ToString("N")[..8].ToUpperInvariant()}",
            paymentType,
            request.CustomerId,
            customer.Name,
            request.PaymentDate,
            paymentMethod,
            request.AmountSyp,
            request.AmountUsd,
            0,
            0,
            false));
    }
}
