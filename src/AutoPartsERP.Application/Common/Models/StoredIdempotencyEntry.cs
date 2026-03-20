namespace AutoPartsERP.Application.Common.Models;

public sealed record StoredIdempotencyEntry(
    string Key,
    Guid UserId,
    string Endpoint,
    string RequestHash,
    string Status,
    string? ResponseJson,
    DateTimeOffset CreatedAtUtc);
