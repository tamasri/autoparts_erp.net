namespace AutoPartsERP.Contracts.Common;

/// <summary>One line of a sales or purchase return: which line of the original it gives back, how much, and (sales) where the goods go.</summary>
public sealed record ReturnLineRequest(Guid LineId, decimal Quantity, Guid? LocationId = null);

public sealed record CreateReturnRequest(DateOnly ReturnDate, string? Reason, IReadOnlyList<ReturnLineRequest> Lines);

/// <summary>A line of the original with what posted returns already took back and what may still be returned, at the original price.</summary>
public sealed record ReturnableLineDto(
    Guid LineId, int LineNumber, string Code, string Name, decimal Quantity, decimal Returned, decimal Returnable,
    decimal UnitPriceUsd, decimal DiscountPct, Guid? LocationId);

/// <summary>A document linked to another: a return and the invoice it returns.</summary>
public sealed record DocumentLinkDto(Guid Id, string Number, string Status, DateOnly Date, decimal TotalUsd);
