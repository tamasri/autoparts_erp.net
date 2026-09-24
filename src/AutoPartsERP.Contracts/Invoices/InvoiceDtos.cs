using AutoPartsERP.Contracts.Common;

namespace AutoPartsERP.Contracts.Invoices;

public sealed record InvoiceLineDto(
    Guid Id,
    int LineNumber,
    Guid SkuId,
    string SkuCode,
    string SkuName,
    Guid? BatchId,
    Guid LocationId,
    decimal Quantity,
    decimal UnitPriceSyp,
    decimal UnitPriceUsd,
    decimal DiscountPct,
    decimal LineTotalSyp,
    decimal LineTotalUsd,
    decimal GrossMarginSyp,
    decimal GrossMarginUsd,
    decimal GrossMarginPct,
    bool IsPriceOverride,
    string? OverrideReason,
    Guid? ReturnOfLineId);

public sealed record InvoiceListItemDto(
    Guid Id,
    string InvoiceNumber,
    string Status,
    string Type,
    Guid CustomerId,
    string CustomerName,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    decimal TotalSyp,
    decimal TotalUsd,
    decimal BalanceSyp,
    decimal BalanceUsd,
    string StatusDisplay,
    string TypeDisplay,
    string DueDateDisplay);

public sealed record InvoiceDto(
    Guid Id,
    string InvoiceNumber,
    string Status,
    string Type,
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    decimal TotalSyp,
    decimal TotalUsd,
    decimal PaidSyp,
    decimal PaidUsd,
    decimal BalanceSyp,
    decimal BalanceUsd,
    string StatusDisplay,
    string TypeDisplay,
    string DueDateDisplay,
    string TotalSypInWords,
    string TotalUsdInWords,
    IReadOnlyCollection<InvoiceLineDto> Lines,
    InvoiceAmountsDto Amounts,
    decimal CreditAppliedSyp,
    decimal CreditAppliedUsd,
    /// <summary>For a return: the sale it returns.</summary>
    DocumentLinkDto? ReturnOf,
    /// <summary>For a sale: the returns made against it.</summary>
    IReadOnlyCollection<DocumentLinkDto> Returns);

/// <summary>
/// How an invoice's total is made up: lines (after their own discounts), minus the invoice discount, plus the delivery fee.
/// <c>DiscountPct</c> is set when the discount is a percentage of the lines; otherwise it is the fixed amount shown.
/// </summary>
public sealed record InvoiceAmountsDto(
    decimal SubtotalSyp,
    decimal SubtotalUsd,
    decimal? DiscountPct,
    decimal DiscountAmountSyp,
    decimal DiscountAmountUsd,
    decimal DeliveryFeeSyp,
    decimal DeliveryFeeUsd);
