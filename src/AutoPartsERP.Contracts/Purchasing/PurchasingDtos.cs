using AutoPartsERP.Contracts.Common;
using AutoPartsERP.Contracts.Customers;

namespace AutoPartsERP.Contracts.Purchasing;

public sealed record PurchaseLineRequest(Guid ItemId, decimal Quantity, decimal UnitCostUsd, decimal DiscountPct);

public sealed record CreatePurchaseInvoiceRequest(
    Guid SupplierPartyId,
    DateOnly BillDate,
    DateOnly DueDate,
    Guid WarehouseId,
    string? SupplierRef,
    Guid? FxRateId,
    string? Notes,
    IReadOnlyList<PurchaseLineRequest> Lines,
    decimal? DiscountPct = null,
    decimal? DiscountAmountUsd = null);

public sealed record VoidPurchaseInvoiceRequest(string Reason);

public sealed record SupplierPaymentAllocationRequest(Guid PurchaseInvoiceId, decimal AmountUsd);

public sealed record CreateSupplierPaymentRequest(
    Guid SupplierPartyId,
    DateOnly PaymentDate,
    string PaymentMethod,
    decimal AmountUsd,
    string? ReferenceNumber,
    string? BankName,
    string? ChequeNumber,
    string? Notes,
    IReadOnlyList<SupplierPaymentAllocationRequest> Allocations);

public sealed record ReverseSupplierPaymentRequest(string Reason);

public sealed record PurchaseInvoiceListDto(
    Guid Id,
    string BillNumber,
    Guid SupplierPartyId,
    string SupplierName,
    string? SupplierRef,
    DateOnly BillDate,
    DateOnly DueDate,
    string Status,
    decimal TotalUsd,
    decimal PaidUsd,
    decimal BalanceUsd,
    bool IsReturn,
    /// <summary>GOODS (stock lines) or SERVICE (landed-cost charges owed to a supplier, raised by a landed cost voucher).</summary>
    string Kind);

public sealed record PurchaseLineDto(
    Guid Id,
    int LineNumber,
    Guid? ItemId,
    string ItemCode,
    string ItemName,
    decimal Quantity,
    decimal UnitCostUsd,
    decimal DiscountPct,
    decimal LineTotalUsd,
    Guid? ReturnOfLineId,
    string? ChargeType);

public sealed record PurchaseInvoiceDetailDto(
    PurchaseInvoiceListDto Invoice,
    Guid? WarehouseId,
    string? Notes,
    string? VoidReason,
    DateTimeOffset? PostedAt,
    IReadOnlyCollection<PurchaseLineDto> Lines,
    decimal SubtotalUsd,
    decimal? DiscountPct,
    decimal DiscountAmountUsd,
    decimal CreditAppliedUsd,
    DocumentLinkDto? ReturnAgainst,
    IReadOnlyCollection<DocumentLinkDto> Returns,
    IReadOnlyCollection<DocumentLinkDto> LandedCosts);

public sealed record SupplierPaymentDto(
    Guid Id,
    string PaymentNumber,
    Guid SupplierPartyId,
    string SupplierName,
    DateOnly PaymentDate,
    string PaymentMethod,
    decimal AmountUsd,
    decimal UnallocatedUsd,
    bool IsReversed);

public sealed record SupplierStatementDto(
    Guid PartyId,
    decimal TotalBilledUsd,
    decimal TotalPaidUsd,
    decimal OutstandingUsd,
    IReadOnlyCollection<CustomerStatementTransactionDto> Transactions);
