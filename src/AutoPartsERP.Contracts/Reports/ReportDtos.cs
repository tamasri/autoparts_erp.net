namespace AutoPartsERP.Contracts.Reports;

public sealed record ProfitLossRowDto(
    int Year,
    int Month,
    decimal TotalRevenueSyp,
    decimal TotalRevenueUsd,
    decimal TotalCogsSyp,
    decimal TotalCogsUsd,
    decimal GrossProfitSyp,
    decimal GrossProfitUsd,
    decimal GrossMarginPct,
    string PeriodDisplay);

public sealed record InventoryValueRowDto(
    Guid SkuId,
    string SkuCode,
    string SkuName,
    decimal QuantityOnHand,
    decimal QuantityReserved,
    decimal QuantityAvailable,
    decimal UnitCostSyp,
    decimal UnitCostUsd,
    decimal TotalValueSyp,
    decimal TotalValueUsd,
    bool LowStockFlag);

public sealed record BatchTraceRowDto(
    Guid BatchId,
    string BatchNumber,
    string MovementType,
    decimal Quantity,
    string Direction,
    DateTimeOffset CreatedAt,
    string MovementTypeDisplay,
    string TimeAgo);
