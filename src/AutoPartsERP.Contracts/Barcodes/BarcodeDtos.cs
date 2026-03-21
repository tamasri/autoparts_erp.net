namespace AutoPartsERP.Contracts.Barcodes;

public sealed record BarcodeScanResultDto(
    string ScanCode,
    string ScanType,
    Guid? ItemId,
    Guid? BatchId,
    string? ItemPartNumber,
    string? ItemNameAr,
    decimal? AvailableQty,
    string Message);

