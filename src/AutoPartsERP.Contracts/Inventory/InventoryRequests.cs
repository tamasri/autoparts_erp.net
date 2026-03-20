namespace AutoPartsERP.Contracts.Inventory;

public sealed record ReceiveBatchRequest(
    Guid SkuId,
    Guid LocationId,
    decimal Quantity,
    decimal CostPriceSyp,
    decimal CostPriceUsd,
    Guid FxRateId,
    DateOnly ReceivedDate,
    DateOnly? ExpiryDate,
    string? SupplierName,
    string? SupplierInvoice,
    string? Notes);

public sealed record AdjustInventoryRequest(
    Guid SkuId,
    Guid LocationId,
    Guid? BatchId,
    decimal QuantityDelta,
    string Reason,
    string? Notes);

public sealed record TransferStockRequest(
    Guid SkuId,
    Guid FromLocationId,
    Guid ToLocationId,
    Guid? BatchId,
    decimal Quantity,
    string? Notes);

public sealed record InventoryQueryRequest(
    int PageNumber = 1,
    int PageSize = 20,
    Guid? LocationId = null,
    Guid? SkuId = null,
    string? SearchTerm = null);
