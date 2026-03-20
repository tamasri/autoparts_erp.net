namespace AutoPartsERP.Contracts.Parties;

public sealed record CreatePartyRequest(
    string DisplayName,
    string DisplayNameAr,
    string? TaxNumber,
    string? Website,
    string? Notes,
    IReadOnlyCollection<string>? InitialTypeCodes);

public sealed record UpdatePartyRequest(
    string DisplayName,
    string DisplayNameAr,
    string? TaxNumber,
    string? Website,
    string? Notes,
    bool IsActive);

public sealed record RequestPartyTypeAssignmentRequest(
    string TypeCode,
    string Reason);

public sealed record PartyQueryRequest(
    int PageNumber = 1,
    int PageSize = 20,
    string? TypeCode = null,
    bool? IsActive = null,
    string? SearchTerm = null,
    bool? HasCombinedStatement = null);
