using AutoPartsERP.Application.Features.Catalog.CreateSku;
using Dapper;

namespace AutoPartsERP.Application.Features.Items.ImportItems;

/// <summary>
/// Bulk item import. Every row goes through the same <see cref="CreateSkuCommand"/> as a manual entry (validation, audit, idempotency),
/// so an import can never create something the screen would refuse. A dry run validates and reports without writing.
/// </summary>
public sealed record ImportItemsCommand(IReadOnlyList<ImportItemRow> Rows, bool DryRun, string FileKey)
    : IRequest<Result<ImportItemsResult>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Catalog.Write;
}

public sealed class ImportItemsCommandValidator : AbstractValidator<ImportItemsCommand>
{
    public const int MaxRows = 5000;

    public ImportItemsCommandValidator()
    {
        RuleFor(x => x.Rows).NotEmpty().WithMessage("The file has no rows.");
        RuleFor(x => x.Rows.Count).LessThanOrEqualTo(MaxRows).WithMessage($"At most {MaxRows} rows per file.");
        RuleFor(x => x.FileKey).NotEmpty();
    }
}

public sealed class ImportItemsCommandHandler : IRequestHandler<ImportItemsCommand, Result<ImportItemsResult>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ISender _sender;

    public ImportItemsCommandHandler(IDbConnectionFactory connectionFactory, ISender sender)
    {
        _connectionFactory = connectionFactory;
        _sender = sender;
    }

    public async Task<Result<ImportItemsResult>> Handle(ImportItemsCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var categories = (await connection.QueryAsync<(Guid Id, string Name, string NameAr)>(new CommandDefinition(
            "SELECT id AS Id, name AS Name, name_ar AS NameAr FROM categories WHERE is_active ORDER BY depth, name;", cancellationToken: cancellationToken))).ToList();
        if (categories.Count == 0)
        {
            return Result<ImportItemsResult>.Failure(new Error("Import.NoCategory", "Create at least one category before importing items."));
        }

        var existingCodes = (await connection.QueryAsync<string>(new CommandDefinition("SELECT upper(code) FROM skus;", cancellationToken: cancellationToken))).ToHashSet();
        var existingBarcodes = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT barcode FROM skus WHERE barcode IS NOT NULL;", cancellationToken: cancellationToken))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fxMid = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            "SELECT mid_rate FROM fx_rates WHERE is_active ORDER BY rate_date DESC, created_at DESC LIMIT 1;", cancellationToken: cancellationToken));

        var results = new List<ImportRowResult>();
        var seenCodes = new HashSet<string>();
        var created = 0;
        var duplicates = 0;
        var failed = 0;

        foreach (var row in request.Rows)
        {
            var code = row.Code?.Trim().ToUpperInvariant();

            string? Problem()
            {
                if (!string.IsNullOrEmpty(row.ParseError)) return row.ParseError;
                if (string.IsNullOrWhiteSpace(code)) return "رقم القطعة (Code) مطلوب";
                if (string.IsNullOrWhiteSpace(row.Name) && string.IsNullOrWhiteSpace(row.NameAr)) return "الاسم مطلوب (عربي أو إنجليزي)";
                if (row.PriceUsd < 0 || row.PriceSyp < 0 || row.MinPriceUsd < 0) return "الأسعار لا يمكن أن تكون سالبة";
                if (row.WarrantyMonths < 0) return "مدة الضمان لا يمكن أن تكون سالبة";
                return null;
            }

            var problem = Problem();
            if (problem is not null)
            {
                failed++;
                results.Add(new ImportRowResult(row.RowNumber, code, "ERROR", problem));
                continue;
            }

            if (existingCodes.Contains(code!) || !seenCodes.Add(code!))
            {
                duplicates++;
                results.Add(new ImportRowResult(row.RowNumber, code, "DUPLICATE", existingCodes.Contains(code!) ? "الصنف موجود مسبقاً — تم تجاوزه" : "مكرر داخل الملف — تم تجاوزه"));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(row.Barcode) && existingBarcodes.Contains(row.Barcode.Trim()))
            {
                failed++;
                results.Add(new ImportRowResult(row.RowNumber, code, "ERROR", "الباركود مستخدم لصنف آخر"));
                continue;
            }

            Guid categoryId;
            if (string.IsNullOrWhiteSpace(row.Category))
            {
                categoryId = categories[0].Id;
            }
            else
            {
                var wanted = row.Category.Trim();
                var match = categories.FirstOrDefault(c => string.Equals(c.Name, wanted, StringComparison.OrdinalIgnoreCase) || string.Equals(c.NameAr, wanted, StringComparison.OrdinalIgnoreCase));
                if (match.Id == Guid.Empty)
                {
                    failed++;
                    results.Add(new ImportRowResult(row.RowNumber, code, "ERROR", $"الفئة '{wanted}' غير موجودة"));
                    continue;
                }

                categoryId = match.Id;
            }

            var name = string.IsNullOrWhiteSpace(row.Name) ? row.NameAr!.Trim() : row.Name.Trim();
            var nameAr = string.IsNullOrWhiteSpace(row.NameAr) ? name : row.NameAr.Trim();
            var usd = row.PriceUsd ?? 0m;
            var syp = row.PriceSyp ?? (fxMid.HasValue ? Math.Round(usd * fxMid.Value, 2) : 0m);

            if (request.DryRun)
            {
                created++;
                results.Add(new ImportRowResult(row.RowNumber, code, "OK", "جاهز للاستيراد"));
                continue;
            }

            var warranty = row.WarrantyMonths ?? 0;
            var create = await _sender.Send(new CreateSkuCommand(
                new CreateSkuRequest(code!, name, nameAr, categoryId, string.IsNullOrWhiteSpace(row.Barcode) ? null : row.Barcode.Trim(),
                    syp, usd, 0m, 0m, false, warranty > 0, warranty, null),
                $"import-{request.FileKey}-{row.RowNumber}"), cancellationToken);

            if (create.IsSuccess)
            {
                created++;
                existingCodes.Add(code!);
                if (!string.IsNullOrWhiteSpace(row.Barcode)) existingBarcodes.Add(row.Barcode.Trim());
                results.Add(new ImportRowResult(row.RowNumber, code, "OK", "تم الإنشاء"));
            }
            else
            {
                failed++;
                results.Add(new ImportRowResult(row.RowNumber, code, "ERROR", create.Error.Message));
            }
        }

        if (!request.DryRun && created > 0)
        {
            // Create the warehouse-side item rows for the new SKUs right away, so they can be received and stocked at once.
            await connection.ExecuteAsync(new CommandDefinition("SELECT sync_items_from_skus();", cancellationToken: cancellationToken));
        }

        return Result<ImportItemsResult>.Success(new ImportItemsResult(request.DryRun, request.Rows.Count, created, duplicates, failed, results));
    }
}
