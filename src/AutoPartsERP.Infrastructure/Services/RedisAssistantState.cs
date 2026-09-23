using AutoPartsERP.Application.Features.Assistant;

namespace AutoPartsERP.Infrastructure.Services;

/// <summary>Assistant state in Redis: atomic SET NX for duplicate messages, INCR+EXPIRE counters, JSON values with a time-to-live.</summary>
public sealed class RedisAssistantState : IAssistantState
{
    private static readonly TimeSpan PendingLife = TimeSpan.FromMinutes(10);
    private const string GatewayKey = "assistant:gateway";

    private readonly IConnectionMultiplexer _redis;

    public RedisAssistantState(IConnectionMultiplexer redis) => _redis = redis;

    private IDatabase Db => _redis.GetDatabase();

    public Task<bool> FirstSeenAsync(string messageId, CancellationToken cancellationToken = default) =>
        Db.StringSetAsync($"assistant:seen:{messageId}", 1, TimeSpan.FromDays(1), When.NotExists);

    public async Task<bool> AllowAsync(string key, int limit, TimeSpan window, CancellationToken cancellationToken = default)
    {
        var count = await Db.StringIncrementAsync(key);
        // Set on every hit but only when missing, so a counter can never be left without an expiry.
        await Db.KeyExpireAsync(key, window, ExpireWhen.HasNoExpiry);
        return count <= limit;
    }

    public async Task<PendingChoice?> GetPendingAsync(Guid linkId, CancellationToken cancellationToken = default)
    {
        var value = await Db.StringGetAsync($"assistant:pending:{linkId}");
        return value.HasValue ? JsonSerializer.Deserialize<PendingChoice>(value.ToString()) : null;
    }

    public Task SetPendingAsync(Guid linkId, PendingChoice choice, CancellationToken cancellationToken = default) =>
        Db.StringSetAsync($"assistant:pending:{linkId}", JsonSerializer.Serialize(choice), PendingLife);

    public Task ClearPendingAsync(Guid linkId, CancellationToken cancellationToken = default) =>
        Db.KeyDeleteAsync($"assistant:pending:{linkId}");

    public async Task<GatewayStatus?> GetGatewayStatusAsync(CancellationToken cancellationToken = default)
    {
        var value = await Db.StringGetAsync(GatewayKey);
        return value.HasValue ? JsonSerializer.Deserialize<GatewayStatus>(value.ToString()) : null;
    }

    public Task SetGatewayStatusAsync(GatewayStatus status, CancellationToken cancellationToken = default) =>
        Db.StringSetAsync(GatewayKey, JsonSerializer.Serialize(status), TimeSpan.FromHours(1));
}
