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

    public UpdateDeliveryFeeCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(UpdateDeliveryFeeCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE invoices
            SET delivery_fee_syp = @DeliveryFeeSyp,
                delivery_fee_usd = @DeliveryFeeUsd,
                total_syp = subtotal_syp - discount_amount_syp + @DeliveryFeeSyp + tax_amount_syp,
                total_usd = subtotal_usd - discount_amount_usd + @DeliveryFeeUsd + tax_amount_usd,
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

        return updated == 0
            ? Result<Guid>.Failure(new Error("Invoice.InvalidState", "Delivery fee can only be updated on draft invoices."))
            : Result<Guid>.Success(request.InvoiceId);
    }
}
