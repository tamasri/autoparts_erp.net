using AutoPartsERP.Contracts.Common;

namespace AutoPartsERP.Contracts.Purchasing;

/// <summary>
/// One landed cost: its type (FREIGHT, CUSTOMS, SHIPPING, INSURANCE, OTHER), how it is split over the goods (VALUE, QTY, EQUAL), and
/// how it is settled — owed to a supplier (who gets a service bill) or paid at once from a cash/bank account; exactly one of the two.
/// </summary>
public sealed record LandedCostChargeRequest(
    string ChargeType, string? Description, decimal AmountUsd, string SplitMethod, Guid? SupplierPartyId, string? PaidFromAccount);

public sealed record SaveLandedCostRequest(
    DateOnly VoucherDate, Guid? FxRateId, string? Notes, IReadOnlyList<Guid> PurchaseInvoiceIds, IReadOnlyList<LandedCostChargeRequest> Charges);

public sealed record VoidLandedCostRequest(string Reason);

public sealed record LandedCostListDto(Guid Id, string VoucherNumber, DateOnly VoucherDate, string Status, decimal TotalUsd, string BillNumbers, int ChargeCount);

public sealed record LandedCostChargeDto(
    Guid Id, int LineNumber, string ChargeType, string? Description, decimal AmountUsd, string SplitMethod,
    Guid? SupplierPartyId, string? SupplierName, string? PaidFromAccount, DocumentLinkDto? ServiceBill);

/// <summary>How the charges fell on one bill line (<c>ByCharge</c>: charge id → amount) and what one unit costs with them.</summary>
public sealed record LandedCostLineDto(
    Guid PurchaseInvoiceLineId, string BillNumber, string ItemCode, string ItemName, decimal Quantity, decimal NetUnitCostUsd,
    decimal AllocatedUsd, decimal LandedUnitCostUsd, IReadOnlyDictionary<Guid, decimal> ByCharge);

/// <summary>What the voucher does (draft: would do) to one item's average cost: capitalized on the stock on hand, expensed for the part sold.</summary>
public sealed record LandedCostItemEffectDto(
    Guid SkuId, string ItemCode, string ItemName, decimal Quantity, decimal OnHand, decimal AllocatedUsd, decimal CapitalizedUsd, decimal ExpensedUsd,
    decimal CostBeforeUsd, decimal CostAfterUsd);

/// <summary><c>IsPreview</c>: a draft, worked out on today's stock and costs; once posted the figures are the ones that were booked.</summary>
public sealed record LandedCostDetailDto(
    LandedCostListDto Voucher, Guid? FxRateId, string? Notes, string? VoidReason, IReadOnlyList<DocumentLinkDto> Bills, IReadOnlyList<LandedCostChargeDto> Charges,
    IReadOnlyList<LandedCostLineDto> Lines, IReadOnlyList<LandedCostItemEffectDto> Effects, bool IsPreview, string? PreviewProblem);
