namespace AutoPartsERP.Contracts.Parties;

public sealed record PartyListItemDto(
    Guid Id,
    string Code,
    string DisplayName,
    string DisplayNameAr,
    bool IsActive,
    bool HasCombinedStatement,
    IReadOnlyCollection<string> ActiveTypeCodes,
    DateTimeOffset CreatedAt);

public sealed record PartyTypeAssignmentDto(
    Guid Id,
    string TypeCode,
    bool IsActive,
    Guid RequestedBy,
    Guid? ApprovedBy,
    Guid? ApprovalId,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? DeactivatedAt,
    DateTimeOffset CreatedAt);

public sealed record PartyContactDto(
    Guid Id,
    string Type,
    string Value,
    string? Label,
    bool IsPrimary,
    DateTimeOffset CreatedAt);

public sealed record PartyAddressDto(
    Guid Id,
    string Type,
    string Line1,
    string? Line2,
    string? City,
    string? Region,
    string Country,
    bool IsDefault,
    DateTimeOffset CreatedAt);

public sealed record PartyNoteDto(
    Guid Id,
    string Content,
    bool IsPinned,
    DateTimeOffset CreatedAt);

public sealed record PartyProfileDto(
    Guid Id,
    string Code,
    string DisplayName,
    string DisplayNameAr,
    string? TaxNumber,
    string? Website,
    string? Notes,
    bool IsActive,
    bool HasCombinedStatement,
    bool ShowArTab,
    bool ShowApTab,
    bool ShowHrTab,
    IReadOnlyCollection<PartyTypeAssignmentDto> TypeAssignments,
    IReadOnlyCollection<PartyContactDto> Contacts,
    IReadOnlyCollection<PartyAddressDto> Addresses,
    IReadOnlyCollection<PartyNoteDto> NotesEntries,
    DateTimeOffset CreatedAt);

public sealed record CombinedStatementLineDto(
    DateOnly Date,
    string EntryType,
    string ReferenceNumber,
    string Description,
    decimal DebitSyp,
    decimal CreditSyp,
    decimal DebitUsd,
    decimal CreditUsd);

public sealed record CombinedStatementBalanceDto(
    decimal TotalDebitSyp,
    decimal TotalCreditSyp,
    decimal OutstandingSyp,
    decimal TotalDebitUsd,
    decimal TotalCreditUsd,
    decimal OutstandingUsd);

public sealed record CombinedStatementDto(
    Guid PartyId,
    IReadOnlyCollection<CombinedStatementLineDto> ArLines,
    IReadOnlyCollection<CombinedStatementLineDto> ApLines,
    CombinedStatementBalanceDto ArBalance,
    CombinedStatementBalanceDto ApBalance,
    CombinedStatementBalanceDto NetPosition);
