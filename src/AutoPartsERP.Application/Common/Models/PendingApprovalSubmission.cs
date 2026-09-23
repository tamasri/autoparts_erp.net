namespace AutoPartsERP.Application.Common.Models;

/// <summary>
/// A request held for approval. <c>ScopeWarehouseIds</c> is set for warehouse transfers: their approvers are the managers of those warehouses,
/// and <c>RequiredApprovals</c> then counts the warehouses still to consent (shown in the inbox; completion is decided per warehouse).
/// </summary>
public sealed record PendingApprovalSubmission(
    Guid CorrelationId,
    string RequestType,
    string EntityType,
    string? EntityId,
    string PayloadJson,
    Guid RequesterId,
    string? RequesterNotes,
    string? ReasonCode,
    string Module,
    int RequiredApprovals = 1,
    IReadOnlyList<Guid>? ScopeWarehouseIds = null);
