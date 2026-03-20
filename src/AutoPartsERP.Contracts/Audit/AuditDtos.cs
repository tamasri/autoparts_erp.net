namespace AutoPartsERP.Contracts.Audit;

public sealed record AuditEntryDto(
    Guid Id,
    string Action,
    string EntityType,
    string EntityId,
    Guid? ActorUserId,
    string? ActorName,
    string? CorrelationId,
    string? IpAddress,
    string? Details,
    DateTimeOffset OccurredAtUtc);
