using AutoPartsERP.Contracts.Invoices;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AutoPartsERP.Infrastructure.Documents;

public sealed class InvoiceDocument : IDocument
{
    private readonly InvoiceDto _invoice;

    public InvoiceDocument(InvoiceDto invoice)
    {
        _invoice = invoice;
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(20);
            page.DefaultTextStyle(x => x.FontFamily("Noto Kufi Arabic").FontSize(11));

            page.Header().Text($"فاتورة {_invoice.InvoiceNumber}").SemiBold().FontSize(18);

            page.Content().Column(col =>
            {
                col.Spacing(8);
                col.Item().Text($"العميل: {_invoice.CustomerName}");
                col.Item().Text($"التاريخ: {_invoice.InvoiceDate:yyyy-MM-dd}");

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.ConstantColumn(80);
                        columns.ConstantColumn(80);
                        columns.ConstantColumn(100);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Text("الصنف");
                        header.Cell().Text("الكمية");
                        header.Cell().Text("السعر");
                        header.Cell().Text("الإجمالي");
                    });

                    foreach (var line in _invoice.Lines)
                    {
                        table.Cell().Text(line.SkuName);
                        table.Cell().Text(line.Quantity.ToString("0.####"));
                        table.Cell().Text(line.UnitPriceSyp.ToString("0.0000"));
                        table.Cell().Text(line.LineTotalSyp.ToString("0.0000"));
                    }
                });

                col.Item().Text($"الإجمالي (ل.س): {_invoice.TotalSyp:0.0000}");
                col.Item().Text($"الإجمالي (دولار): {_invoice.TotalUsd:0.0000}");
                col.Item().Text(_invoice.TotalSypInWords);
                col.Item().Text(_invoice.TotalUsdInWords);
            });

            page.Footer().AlignCenter().Text("ملاحظة: تخضع البنود المشمولة للضمان لسياسة الضمان المعتمدة.");
        });
    }
}
