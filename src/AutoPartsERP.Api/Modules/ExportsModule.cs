using AutoPartsERP.Application.Features.CompanyProfile;

namespace AutoPartsERP.Api.Modules;

/// <summary>
/// Generic print/export endpoint: the screen sends the document or list it is showing and receives a PDF, Excel or CSV file.
/// One engine means every document (transfers, issue orders, counts, adjustments, receipts, statements, item movements)
/// prints and exports the same way. The content comes from the caller's own view, so it never exposes more than the caller
/// could already read on screen.
/// </summary>
public sealed class ExportsModule : ICarterModule
{
    private const int MaxRowsPerTable = 20_000;
    private const int MaxColumns = 40;

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/exports/{format}", async Task<IResult> (string format, ExportDocument document, IDocumentRenderer renderer, ISender sender, CancellationToken ct) =>
            {
                if (document is null || string.IsNullOrWhiteSpace(document.Title))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Export.Invalid", detail: "A title is required.");
                }

                if (document.Tables.Any(t => t.Columns.Count > MaxColumns || t.Rows.Count > MaxRowsPerTable))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Export.TooLarge", detail: $"At most {MaxColumns} columns and {MaxRowsPerTable} rows per table.");
                }

                var fileBase = SafeFileName(document.FileName ?? document.Title);
                switch (format.ToLowerInvariant())
                {
                    case "pdf":
                        var company = (await sender.Send(new GetCompanyProfileQuery(), ct)).Value ?? CompanyProfileDto.Empty;
                        return Results.File(renderer.ToPdf(document, company), "application/pdf", $"{fileBase}.pdf");
                    case "xlsx":
                        return Results.File(renderer.ToXlsx(document), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{fileBase}.xlsx");
                    case "csv":
                        return Results.File(renderer.ToCsv(document), "text/csv; charset=utf-8", $"{fileBase}.csv");
                    default:
                        return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Export.Format", detail: "Format must be pdf, xlsx or csv.");
                }
            })
            .RequireAuthorization()
            .RequireRateLimiting(RateLimiting.Heavy);
    }

    private static string SafeFileName(string raw)
    {
        var cleaned = new string(raw.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or ' ').ToArray()).Trim().Replace(' ', '-');
        return cleaned.Length == 0 ? "export" : cleaned[..Math.Min(cleaned.Length, 80)];
    }
}
