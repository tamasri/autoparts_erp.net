namespace AutoPartsERP.Contracts.Audit;

public sealed record AuditSearchRequest(
    string? Action,
    string? EntityType,
    string? EntityId,
    Guid? ActorUserId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int PageNumber = 1,
    int PageSize = 50);
