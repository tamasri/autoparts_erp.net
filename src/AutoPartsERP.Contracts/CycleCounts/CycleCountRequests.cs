namespace AutoPartsERP.Contracts.CycleCounts;

public sealed record CreateCycleCountPlanRequest(
    Guid WarehouseId,
    string ScopeType,
    string? ScopeFilterJson,
    DateOnly ScheduledFor);

public sealed record RecordCycleCountRequest(
    Guid CycleCountPlanId,
    IReadOnlyCollection<RecordCycleCountLineRequest> Lines);

public sealed record RecordCycleCountLineRequest(
    Guid LineId,
    decimal CountedQty);

