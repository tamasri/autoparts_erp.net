using AutoPartsERP.Contracts.Exports;

namespace AutoPartsERP.Application.Common.Abstractions;

/// <summary>Renders any <see cref="ExportDocument"/> (a printed document or a list) as PDF, Excel or CSV.</summary>
public interface IDocumentRenderer
{
    byte[] ToPdf(ExportDocument document);

    byte[] ToXlsx(ExportDocument document);

    byte[] ToCsv(ExportDocument document);
}
