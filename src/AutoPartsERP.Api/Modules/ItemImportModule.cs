using System.Security.Cryptography;
using AutoPartsERP.Application.Features.Items.ImportItems;
using AutoPartsERP.Infrastructure.Imports;

namespace AutoPartsERP.Api.Modules;

/// <summary>Item import from Excel or CSV: a blank template, a dry run that reports every row, and the real import.</summary>
public sealed class ItemImportModule : ICarterModule
{
    private const long MaxFileBytes = 5 * 1024 * 1024;

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/items/import").RequireAuthorization();

        group.MapGet("/template", (string? format) =>
            string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
                ? Results.File(ItemFileReader.BuildTemplateCsv(), "text/csv; charset=utf-8", "items-template.csv")
                : Results.File(ItemFileReader.BuildTemplateXlsx(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "items-template.xlsx"));

        group.MapPost("/", async Task<IResult> (IFormFile file, bool? dryRun, ISender sender, CancellationToken cancellationToken) =>
            {
                if (file is null || file.Length == 0)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Import.NoFile", detail: "Choose a .xlsx or .csv file.");
                }

                if (file.Length > MaxFileBytes)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Import.TooLarge", detail: "The file is larger than 5 MB.");
                }

                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (extension is not (".xlsx" or ".csv"))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Import.Format", detail: "Only .xlsx and .csv files are supported.");
                }

                await using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, cancellationToken);
                var fileKey = Convert.ToHexString(SHA256.HashData(buffer.ToArray()))[..16];
                buffer.Position = 0;

                IReadOnlyList<ImportItemRow> rows;
                try
                {
                    rows = ItemFileReader.Read(buffer, file.FileName);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Import.Unreadable", detail: ex is InvalidDataException ? ex.Message : "The file could not be read.");
                }

                var result = await sender.Send(new ImportItemsCommand(rows, dryRun ?? true, fileKey), cancellationToken);
                return result.ToApiResult();
            })
            .DisableAntiforgery();
    }
}
