using System.Globalization;
using AutoPartsERP.Application.Features.Invoices.GetInvoiceById;

namespace AutoPartsERP.Application.Features.Invoices.GetInvoicePdf;

public sealed record GetInvoicePdfQuery(Guid InvoiceId)
    : IRequest<Result<byte[]>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.Read;
}

/// <summary>Renders the invoice through the shared document engine (real PDF, embedded Arabic font, RTL).</summary>
public sealed class GetInvoicePdfQueryHandler : IRequestHandler<GetInvoicePdfQuery, Result<byte[]>>
{
    private readonly ISender _sender;
    private readonly IDocumentRenderer _renderer;

    public GetInvoicePdfQueryHandler(ISender sender, IDocumentRenderer renderer)
    {
        _sender = sender;
        _renderer = renderer;
    }

    public async Task<Result<byte[]>> Handle(GetInvoicePdfQuery request, CancellationToken cancellationToken)
    {
        var loaded = await _sender.Send(new GetInvoiceByIdQuery(request.InvoiceId), cancellationToken);
        if (loaded.IsFailure)
        {
            return Result<byte[]>.Failure(loaded.Error);
        }

        var invoice = loaded.Value!;
        string N(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        var document = new ExportDocument(
            $"فاتورة {invoice.InvoiceNumber}",
            $"{invoice.TypeDisplay} · {invoice.StatusDisplay}",
            [
                new ExportField("الزبون", $"{invoice.CustomerName} ({invoice.CustomerCode})"),
                new ExportField("تاريخ الفاتورة", invoice.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                new ExportField("تاريخ الاستحقاق", invoice.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                new ExportField("مجموع البنود ($)", N(Math.Abs(invoice.Amounts.SubtotalUsd))),
                new ExportField(invoice.Amounts.DiscountPct is { } pct ? $"خصم الفاتورة {N(pct)}% ($)" : "خصم الفاتورة ($)", invoice.Amounts.DiscountAmountUsd == 0 ? null : N(invoice.Amounts.DiscountAmountUsd)),
                new ExportField("أجور التوصيل ($)", invoice.Amounts.DeliveryFeeUsd == 0 ? null : N(invoice.Amounts.DeliveryFeeUsd)),
                new ExportField("الإجمالي (ل.س)", N(invoice.TotalSyp)),
                new ExportField("الإجمالي ($)", N(invoice.TotalUsd)),
                new ExportField("المتبقي ($)", N(invoice.BalanceUsd))
            ],
            [
                new ExportTable(
                    "بنود الفاتورة",
                    ["#", "الرمز", "الصنف", "الكمية", "السعر (ل.س)", "السعر ($)", "الخصم %", "الإجمالي (ل.س)", "الإجمالي ($)"],
                    invoice.Lines.Select(l => (IReadOnlyList<string?>)new List<string?>
                    {
                        l.LineNumber.ToString(CultureInfo.InvariantCulture), l.SkuCode, l.SkuName, N(l.Quantity), N(l.UnitPriceSyp), N(l.UnitPriceUsd),
                        N(l.DiscountPct), N(l.LineTotalSyp), N(l.LineTotalUsd)
                    }).ToList(),
                    ["", "", "الإجمالي", "", "", "", "", N(invoice.TotalSyp), N(invoice.TotalUsd)],
                    [3, 4, 5, 6, 7, 8])
            ],
            string.IsNullOrWhiteSpace(invoice.TotalUsdInWords) ? null : $"المبلغ كتابةً: {invoice.TotalUsdInWords}",
            $"invoice-{invoice.InvoiceNumber}");

        return Result<byte[]>.Success(_renderer.ToPdf(document));
    }
}
