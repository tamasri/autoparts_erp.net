using AutoPartsERP.Contracts.Exports;
using AutoPartsERP.Contracts.Settings;

namespace AutoPartsERP.Application.Common.Abstractions;

/// <summary>Renders any <see cref="ExportDocument"/> (a printed document or a list) as PDF, Excel or CSV.</summary>
public interface IDocumentRenderer
{
    /// <summary>The printed form, with the company's own details (<paramref name="company"/>) in the letterhead and footer.</summary>
    byte[] ToPdf(ExportDocument document, CompanyProfileDto company);

    byte[] ToXlsx(ExportDocument document);

    byte[] ToCsv(ExportDocument document);
}
