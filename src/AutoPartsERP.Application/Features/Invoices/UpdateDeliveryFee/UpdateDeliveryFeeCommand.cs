using Dapper;

namespace AutoPartsERP.Application.Features.Invoices.UpdateDeliveryFee;

public sealed record UpdateDeliveryFeeCommand(
    Guid InvoiceId,
    decimal DeliveryFeeSyp,
    decimal DeliveryFeeUsd,
    string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.DeliveryFee;
    public string AuditModule => "INVOICES";
}

public sealed class UpdateDeliveryFeeCommandValidator : AbstractValidator<UpdateDeliveryFeeCommand>
{
    public UpdateDeliveryFeeCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.DeliveryFeeSyp).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DeliveryFeeUsd).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateDeliveryFeeCommandHandler : IRequestHandler<UpdateDeliveryFeeCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IPeriodLockService _periodLock;

    public UpdateDeliveryFeeCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, IPeriodLockService periodLock)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _periodLock = periodLock;
    }

    public async Task<Result<Guid>> Handle(UpdateDeliveryFeeCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var open = await InvoicePeriod.EnsureOpenAsync(_periodLock, connection, null, request.InvoiceId, cancellationToken);
        if (open.IsFailure)
        {
            return Result<Guid>.Failure(open.Error);
        }

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE invoices
            SET delivery_fee_syp = @DeliveryFeeSyp,
                delivery_fee_usd = @DeliveryFeeUsd,
                updated_at = now(),
                updated_by = @UpdatedBy
            WHERE id = @InvoiceId
              AND status = 'DRAFT';
            """,
            new
            {
                request.InvoiceId,
                request.DeliveryFeeSyp,
                request.DeliveryFeeUsd,
                UpdatedBy = _currentUser.UserId
            },
            cancellationToken: cancellationToken));

        if (updated == 0)
        {
            return Result<Guid>.Failure(new Error("Invoice.InvalidState", "Delivery fee can only be updated on draft invoices."));
        }

        await InvoiceTotals.RecalculateAsync(connection, null, request.InvoiceId, cancellationToken);
        return Result<Guid>.Success(request.InvoiceId);
    }
}
