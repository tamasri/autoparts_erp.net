namespace AutoPartsERP.Application.Common.Models;

public sealed record IdempotencyCheckResult(bool IsNew, bool IsReplay, bool IsConflict, string? CachedResponse)
{
    public static IdempotencyCheckResult New()
    {
        return new IdempotencyCheckResult(true, false, false, null);
    }

    public static IdempotencyCheckResult Replay(string? cachedResponse)
    {
        return new IdempotencyCheckResult(false, true, false, cachedResponse);
    }

    public static IdempotencyCheckResult Conflict()
    {
        return new IdempotencyCheckResult(false, false, true, null);
    }
}
