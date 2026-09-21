namespace AutoPartsERP.Infrastructure.Services;

public sealed class PeriodLockService : IPeriodLockService
{
    /// <summary>Short on purpose: a lock or unlock forgets the cached answer itself, this only bounds the rare miss.</summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private static readonly string[] Modules = ["SALES", "PURCHASES", "PAYMENTS", "ACCOUNTING", "INVENTORY"];

    private readonly IDistributedCache _cache;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public PeriodLockService(IDistributedCache cache, IDbConnectionFactory dbConnectionFactory)
    {
        _cache = cache;
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<bool> IsLockedAsync(int year, int month, string module, CancellationToken cancellationToken = default)
    {
        var periodKey = $"{year:D4}-{month:D2}";
        var cacheKey = $"period:{module}:{periodKey}";
        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached == "1";
        }

        await using var connection = await _dbConnectionFactory.CreateAsync(cancellationToken);
        var isLocked = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM period_locks WHERE period_key = @PeriodKey AND (module_code = @Module OR module_code = 'ALL') AND is_locked = TRUE);",
            new { PeriodKey = periodKey, Module = module });

        await _cache.SetStringAsync(cacheKey, isLocked ? "1" : "0", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheLifetime
        }, cancellationToken);

        return isLocked;
    }

    public async Task InvalidateCacheAsync(int year, int month, string module, CancellationToken cancellationToken = default)
    {
        var periodKey = $"{year:D4}-{month:D2}";
        await _cache.RemoveAsync($"period:{module}:{periodKey}", cancellationToken);
        await _cache.RemoveAsync($"period:ALL:{periodKey}", cancellationToken);
        if (string.Equals(module, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var known in Modules)
            {
                await _cache.RemoveAsync($"period:{known}:{periodKey}", cancellationToken);
            }
        }
    }
}