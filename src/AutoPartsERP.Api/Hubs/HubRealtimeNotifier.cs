using Microsoft.AspNetCore.SignalR;

namespace AutoPartsERP.Api.Hubs;

/// <summary><see cref="IRealtimeNotifier"/> over SignalR groups assigned by <see cref="ErpHub"/>; failures are logged, never thrown.</summary>
public sealed class HubRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<ErpHub> _hub;
    private readonly ILogger<HubRealtimeNotifier> _logger;

    public HubRealtimeNotifier(IHubContext<ErpHub> hub, ILogger<HubRealtimeNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task ToPermissionAsync(string permission, string eventName, object payload, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients.Group(RealtimeGroups.Permission(permission)).SendAsync(eventName, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Realtime {Event} to permission {Permission} not sent.", eventName, permission);
        }
    }

    public async Task ToUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload, CancellationToken cancellationToken = default)
    {
        var groups = userIds.Distinct().Select(RealtimeGroups.User).ToList();
        if (groups.Count == 0)
        {
            return;
        }

        try
        {
            await _hub.Clients.Groups(groups).SendAsync(eventName, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Realtime {Event} to {Count} user(s) not sent.", eventName, groups.Count);
        }
    }
}
