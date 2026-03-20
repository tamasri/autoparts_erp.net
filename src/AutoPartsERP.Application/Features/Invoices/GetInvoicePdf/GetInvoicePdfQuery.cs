namespace AutoPartsERP.Application.Features.Invoices.GetInvoicePdf;

public sealed record GetInvoicePdfQuery(Guid InvoiceId)
    : IRequest<Result<byte[]>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.Read;
}

public sealed class GetInvoicePdfQueryHandler : IRequestHandler<GetInvoicePdfQuery, Result<byte[]>>
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public GetInvoicePdfQueryHandler(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<Result<byte[]>> Handle(GetInvoicePdfQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _dbConnectionFactory.CreateAsync(cancellationToken);
        var invoice = await connection.QueryFirstOrDefaultAsync<(string InvoiceNumber, DateTime InvoiceDate, decimal TotalSyp)>(
            """
            SELECT invoice_number AS InvoiceNumber, invoice_date AS InvoiceDate, total_syp AS TotalSyp
            FROM invoices
            WHERE id = @Id
            """,
            new { Id = request.InvoiceId });

        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
        {
            return Result<byte[]>.Failure(new Error("Invoices.NotFound", "Invoice was not found."));
        }

        var content = $"Invoice {invoice.InvoiceNumber}\nDate: {invoice.InvoiceDate:yyyy-MM-dd}\nTotal SYP: {invoice.TotalSyp:0.0000}";
        return Result<byte[]>.Success(Encoding.UTF8.GetBytes(content));
    }
}
