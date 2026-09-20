namespace AutoPartsERP.Contracts.Items;

/// <summary>One row of an item import file (already read from Excel/CSV and cleaned by the reader).</summary>
public sealed record ImportItemRow(
    int RowNumber,
    string? Code,
    string? Name,
    string? NameAr,
    string? Category,
    string? Barcode,
    decimal? PriceUsd,
    decimal? PriceSyp,
    decimal? MinPriceUsd,
    int? WarrantyMonths,
    string? ParseError = null);

public sealed record ImportRowResult(int RowNumber, string? Code, string Status, string? Message);

public sealed record ImportItemsResult(bool DryRun, int Total, int Created, int Duplicates, int Failed, IReadOnlyList<ImportRowResult> Rows);
