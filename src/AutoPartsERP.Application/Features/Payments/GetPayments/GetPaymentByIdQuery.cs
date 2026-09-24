namespace AutoPartsERP.Application.Features.Payments.GetPayments;

public sealed record GetPaymentByIdQuery(Guid Id) : IRequest<Result<PaymentDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Payments.Read;
}

public sealed class GetPaymentByIdQueryHandler : IRequestHandler<GetPaymentByIdQuery, Result<PaymentDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetPaymentByIdQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PaymentDto>> Handle(GetPaymentByIdQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var payment = await connection.QuerySingleOrDefaultAsync<PaymentDto>(new CommandDefinition(
            $"{PaymentMappings.Select}\nWHERE p.id = @Id;", new { request.Id }, cancellationToken: cancellationToken));
        return payment is null
            ? Result<PaymentDto>.Failure(new Error("Payment.NotFound", "Payment was not found."))
            : Result<PaymentDto>.Success(PaymentMappings.WithDisplay(payment));
    }
}
