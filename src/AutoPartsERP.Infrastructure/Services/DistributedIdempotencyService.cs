namespace AutoPartsERP.Infrastructure.Services;

public sealed class DistributedIdempotencyService : IIdempotencyService
{
    private readonly IDistributedCache _cache;

    public DistributedIdempotencyService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<IdempotencyCheckResult> CheckAsync(string key, Guid userId, string endpoint, string requestHash, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"idem:{userId}:{key}";
        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            var stored = JsonSerializer.Deserialize<StoredIdempotencyEntry>(cached);
            if (stored is not null)
            {
                if (string.Equals(stored.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                {
                    return IdempotencyCheckResult.Replay(stored.ResponseJson);
                }

                if (string.Equals(stored.Status, "PROCESSING", StringComparison.OrdinalIgnoreCase))
                {
                    return IdempotencyCheckResult.Conflict();
                }
            }
        }

        var processing = new StoredIdempotencyEntry(key, userId, endpoint, requestHash, "PROCESSING", null, DateTimeOffset.UtcNow);
        await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(processing), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
        }, cancellationToken);

        return IdempotencyCheckResult.New();
    }

    public async Task CompleteAsync(string key, Guid userId, object response, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"idem:{userId}:{key}";
        var existing = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (existing is null)
        {
            return;
        }

        var stored = JsonSerializer.Deserialize<StoredIdempotencyEntry>(existing);
        if (stored is null)
        {
            return;
        }

        var completed = stored with { Status = "COMPLETED", ResponseJson = JsonSerializer.Serialize(response) };
        await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(completed), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
        }, cancellationToken);
    }

    public Task FailAsync(string key, Guid userId, CancellationToken cancellationToken = default)
    {
        return _cache.RemoveAsync($"idem:{userId}:{key}", cancellationToken);
    }
}