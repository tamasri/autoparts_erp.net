namespace AutoPartsERP.Contracts.ReasonCodes;

public sealed record ReasonCodeDto(
    Guid Id,
    string Category,
    string Code,
    string Description,
    bool RequiresComment,
    string? AppliesTo,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
