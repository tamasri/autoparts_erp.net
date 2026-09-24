using AutoPartsERP.Application.Features.CompanyProfile;
using AutoPartsERP.Application.Features.Invoices.GetInvoiceById;
using AutoPartsERP.Application.Features.Printing;

namespace AutoPartsERP.Application.Features.Invoices.GetInvoicePdf;

public sealed record GetInvoicePdfQuery(Guid InvoiceId)
    : IRequest<Result<byte[]>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.Read;
}

/// <summary>The printed invoice (sale, return or credit note) in the approved design, with the company's details.</summary>
public sealed class GetInvoicePdfQueryHandler : IRequestHandler<GetInvoicePdfQuery, Result<byte[]>>
{
    private readonly ISender _sender;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IDocumentRenderer _renderer;
    private readonly IAppLinks _links;

    public GetInvoicePdfQueryHandler(ISender sender, IDbConnectionFactory connectionFactory, IDocumentRenderer renderer, IAppLinks links)
    {
        _sender = sender;
        _connectionFactory = connectionFactory;
        _renderer = renderer;
        _links = links;
    }

    public async Task<Result<byte[]>> Handle(GetInvoicePdfQuery request, CancellationToken cancellationToken)
    {
        var loaded = await _sender.Send(new GetInvoiceByIdQuery(request.InvoiceId), cancellationToken);
        if (loaded.IsFailure)
        {
            return Result<byte[]>.Failure(loaded.Error);
        }

        var invoice = loaded.Value!;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var customer = await PrintedCustomer.LoadAsync(connection, invoice.CustomerId, cancellationToken);
        var company = await CompanyProfiles.LoadAsync(connection, cancellationToken);

        return Result<byte[]>.Success(_renderer.ToPdf(InvoicePrint.Build(invoice, customer, company, _links), company));
    }
}
