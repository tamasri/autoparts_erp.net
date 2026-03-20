namespace AutoPartsERP.Application.Common.Models;

public sealed record TokenGenerationRequest(
    Guid UserId,
    string Username,
    string FullName,
    string Email,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);
