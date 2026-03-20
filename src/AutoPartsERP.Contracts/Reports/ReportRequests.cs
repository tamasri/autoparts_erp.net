namespace AutoPartsERP.Contracts.Reports;

public sealed record ProfitLossRequest(int Year, int? Month);

public sealed record InventoryValueRequest(Guid? LocationId = null, Guid? CategoryId = null);

public sealed record BatchTraceRequest(Guid BatchId);
