namespace AutoPartsERP.Contracts.Documents;

/// <summary>A document's place in its series and its neighbours (numbers that were deleted are skipped).</summary>
public sealed record DocumentNeighborsDto(
    string SeriesCode, string SeriesNameAr, long SerialNo, string Number, DocumentRefDto? First, DocumentRefDto? Previous, DocumentRefDto? Next, DocumentRefDto? Last);

public sealed record DocumentRefDto(Guid Id, long SerialNo, string Number);

public sealed record DeleteDocumentRequest(string Reason);

public sealed record DeletedDocumentDto(
    Guid Id, string SeriesCode, string SeriesNameAr, long SerialNo, string DocumentNumber, string DocumentKind, Guid DocumentId,
    string StatusAtDeletion, string Reason, Guid DeletedBy, string? DeletedByName, DateTimeOffset DeletedAt);

/// <summary>
/// One series' numbering at a glance: every number from 1 to <c>LastNumber</c> is either a live document or a recorded deletion,
/// so <c>Unexplained</c> must be 0.
/// </summary>
public sealed record DocumentSeriesHealthDto(string Code, string Prefix, string NameAr, long LastNumber, long Live, long Deleted, long Unexplained);
