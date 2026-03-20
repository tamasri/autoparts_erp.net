namespace AutoPartsERP.Application.Common.Models;

public sealed record PendingApprovalSubmission(
    Guid CorrelationId,
    string RequestType,
    string EntityType,
    string? EntityId,
    string PayloadJson,
    Guid RequesterId,
    string? RequesterNotes,
    string? ReasonCode,
    string Module);
