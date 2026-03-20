namespace AutoPartsERP.Infrastructure.Services;

public sealed class DistributedIdempotencyService : IIdempotencyService
{
    private readonly IDistributedCache _cache;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public DistributedIdempotencyService(IDistributedCache cache, IDbConnectionFactory dbConnectionFactory)
    {
        _cache = cache;
        _dbConnectionFactory = dbConnectionFactory;
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

        // DB fallback for cache misses / restarts
        await using (var connection = await _dbConnectionFactory.CreateAsync(cancellationToken))
        {
            var persisted = await connection.QueryFirstOrDefaultAsync<StoredIdempotencyEntry>(
                """
                SELECT key, CAST(scope AS uuid) AS user_id, '' AS endpoint, request_hash, 
                       CASE WHEN is_completed THEN 'COMPLETED' ELSE 'PROCESSING' END AS status,
                       response_code AS response_json,
                       COALESCE(completed_at_utc, now()) AS created_at_utc
                FROM idempotency_keys
                WHERE key = @Key AND scope = @Scope
                """,
                new { Key = key, Scope = userId.ToString() });

            if (persisted is not null)
            {
                if (string.Equals(persisted.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                {
                    await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(persisted), new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                    }, cancellationToken);
                    return IdempotencyCheckResult.Replay(persisted.ResponseJson);
                }

                return IdempotencyCheckResult.Conflict();
            }
        }

        var processing = new StoredIdempotencyEntry(key, userId, endpoint, requestHash, "PROCESSING", null, DateTimeOffset.UtcNow);
        await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(processing), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
        }, cancellationToken);

        await using (var connection = await _dbConnectionFactory.CreateAsync(cancellationToken))
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO idempotency_keys (id, key, scope, request_hash, expires_at_utc, is_completed)
                VALUES (@Id, @Key, @Scope, @RequestHash, @ExpiresAtUtc, FALSE)
                ON CONFLICT (key, scope) DO NOTHING;
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    Key = key,
                    Scope = userId.ToString(),
                    RequestHash = requestHash,
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(24)
                });
        }

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

        await using var connection = await _dbConnectionFactory.CreateAsync(cancellationToken);
        await connection.ExecuteAsync(
            """
            UPDATE idempotency_keys
            SET is_completed = TRUE,
                completed_at_utc = @CompletedAtUtc,
                response_code = @ResponseCode
            WHERE key = @Key AND scope = @Scope
            """,
            new
            {
                CompletedAtUtc = DateTimeOffset.UtcNow,
                ResponseCode = completed.ResponseJson,
                Key = key,
                Scope = userId.ToString()
            });
    }

    public async Task FailAsync(string key, Guid userId, CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync($"idem:{userId}:{key}", cancellationToken);
        await using var connection = await _dbConnectionFactory.CreateAsync(cancellationToken);
        await connection.ExecuteAsync(
            "DELETE FROM idempotency_keys WHERE key = @Key AND scope = @Scope;",
            new { Key = key, Scope = userId.ToString() });
    }
}
