using Dapper;

namespace AutoPartsERP.Application.Features.Invoices.GetInvoiceById;

public sealed record GetInvoiceByIdQuery(Guid InvoiceId)
    : IRequest<Result<InvoiceDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.Read;
}

public sealed class GetInvoiceByIdQueryValidator : AbstractValidator<GetInvoiceByIdQuery>
{
    public GetInvoiceByIdQueryValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
    }
}

public sealed class GetInvoiceByIdQueryHandler : IRequestHandler<GetInvoiceByIdQuery, Result<InvoiceDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetInvoiceByIdQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<InvoiceDto>> Handle(GetInvoiceByIdQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var invoice = await InvoiceReader.LoadAsync(connection, null, request.InvoiceId, cancellationToken);
        return invoice is null
            ? Result<InvoiceDto>.Failure(new Error("Invoice.NotFound", "Invoice was not found."))
            : Result<InvoiceDto>.Success(invoice);
    }
}
