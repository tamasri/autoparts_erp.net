using AutoPartsERP.Domain.Common;

namespace AutoPartsERP.Domain.Governance;

public sealed class IdempotencyKey : AuditableEntity
{
    public IdempotencyKey(
        Guid id,
        string key,
        string scope,
        string requestHash,
        DateTimeOffset expiresAtUtc)
        : base(id)
    {
        Key = key.Trim();
        Scope = scope.Trim();
        RequestHash = requestHash.Trim();
        ExpiresAtUtc = expiresAtUtc;
    }

    public string Key { get; }

    public string Scope { get; }

    public string RequestHash { get; }

    public DateTimeOffset ExpiresAtUtc { get; }

    public bool IsCompleted { get; private set; }

    public string? ResourceId { get; private set; }

    public string? ResponseCode { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public bool IsExpired => ExpiresAtUtc <= DateTimeOffset.UtcNow;

    public void MarkCompleted(string responseCode, string? resourceId = null)
    {
        IsCompleted = true;
        ResponseCode = responseCode.Trim();
        ResourceId = string.IsNullOrWhiteSpace(resourceId) ? null : resourceId.Trim();
        CompletedAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }
}
