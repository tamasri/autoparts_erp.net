namespace AutoPartsERP.Contracts.Barcodes;

public sealed record BarcodeScanRequest(
    string ScanCode,
    string ScanType,
    string? DeviceId,
    Guid? LocationId);

