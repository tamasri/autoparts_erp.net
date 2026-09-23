using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AutoPartsERP.Api.Hubs;

/// <summary>
/// Real-time channel to signed-in browsers (the access token comes in the query string; see JwtBearerEvents in Program.cs).
/// Clients cannot choose groups themselves: an earlier JoinGroup(name) let any user subscribe to any group's messages.
/// If per-role or per-warehouse groups are needed, the server assigns them in OnConnectedAsync from the user's own claims.
/// </summary>
[Authorize]
public sealed class ErpHub : Hub
{
    private readonly ILogger<ErpHub> _logger;

    public ErpHub(ILogger<ErpHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation(
            "SignalR client connected. ConnectionId={ConnectionId} User={User}",
            Context.ConnectionId,
            Context.User?.Identity?.Name ?? "anonymous");

        await base.OnConnectedAsync();
    }
}
