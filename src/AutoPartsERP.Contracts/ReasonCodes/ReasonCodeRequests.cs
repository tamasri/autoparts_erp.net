namespace AutoPartsERP.Contracts.ReasonCodes;

public sealed record CreateReasonCodeRequest(
    string Category,
    string Code,
    string Description,
    bool RequiresComment,
    string? AppliesTo = null);

public sealed record UpdateReasonCodeRequest(
    string Category,
    string Description,
    bool RequiresComment,
    string? AppliesTo,
    bool IsActive);
