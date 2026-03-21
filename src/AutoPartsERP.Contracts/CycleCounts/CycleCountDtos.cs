namespace AutoPartsERP.Contracts.CycleCounts;

public sealed record CycleCountPlanDto(
    Guid Id,
    Guid WarehouseId,
    string ScopeType,
    string? ScopeFilterJson,
    string Status,
    DateOnly ScheduledFor,
    IReadOnlyCollection<CycleCountLineDto> Lines);

public sealed record CycleCountLineDto(
    Guid Id,
    Guid CycleCountPlanId,
    Guid ItemId,
    Guid LocationId,
    decimal SystemQty,
    decimal? CountedQty,
    decimal VarianceQty,
    string? ReasonCode,
    string? Notes);

