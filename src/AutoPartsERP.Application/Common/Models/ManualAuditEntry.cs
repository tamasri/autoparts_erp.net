namespace AutoPartsERP.Application.Common.Models;

public sealed record ManualAuditEntry(
    Guid CorrelationId,
    string EventType,
    string Module,
    string EntityType,
    object? EntityId,
    Guid ActorId,
    string ActorUsername,
    string Action,
    string Status,
    string? ReasonCode = null,
    string? ReasonNotes = null,
    string? IpAddress = null,
    string? UserAgent = null,
    string? RejectionReason = null);
