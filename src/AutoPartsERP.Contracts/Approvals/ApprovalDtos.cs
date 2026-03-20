namespace AutoPartsERP.Contracts.Approvals;

public sealed record ApprovalDecisionDto(
    Guid Id,
    Guid ReviewerUserId,
    string Status,
    string? Comment,
    DateTimeOffset ReviewedAtUtc);

public sealed record ApprovalRequestDto(
    Guid Id,
    string EntityType,
    string EntityId,
    string ActionCode,
    string Reason,
    string Status,
    Guid RequestedByUserId,
    int RequiredApprovals,
    int CurrentApprovals,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyCollection<ApprovalDecisionDto> Decisions);
