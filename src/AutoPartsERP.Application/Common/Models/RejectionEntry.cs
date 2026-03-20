namespace AutoPartsERP.Application.Common.Models;

public sealed record RejectionEntry(
    Guid CorrelationId,
    Guid UserId,
    string Username,
    string Endpoint,
    string PermissionRequired,
    string Reason,
    string? IpAddress);
