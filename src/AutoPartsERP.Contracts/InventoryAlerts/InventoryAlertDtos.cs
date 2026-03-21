namespace AutoPartsERP.Contracts.InventoryAlerts;

public sealed record InventoryAlertDto(
    Guid Id,
    Guid ItemId,
    string AlertType,
    string Severity,
    string Message,
    decimal? ThresholdValue,
    decimal? CurrentValue,
    string Status,
    Guid? AcknowledgedBy,
    DateTimeOffset? AcknowledgedAt,
    Guid? ResolvedBy,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNote,
    DateTimeOffset CreatedAt);

