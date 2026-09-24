using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AutoPartsERP.Api.Hubs;

/// <summary>
/// Real-time channel to signed-in browsers (the access token comes in the query string; see JwtBearerEvents in Program.cs).
/// The server decides what a connection receives: on connect it joins its user's group and one group per notification permission
/// the user holds (<see cref="RealtimeGroups"/>). Clients have no way to join groups themselves (an earlier JoinGroup(name) let any
/// user subscribe to any group). Permissions are read from the token, so a changed role applies from the next sign-in / refresh.
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
        var user = Context.User;
        if (Guid.TryParse(user?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value, out var userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.User(userId));
        }

        var held = user?.FindAll("permission").Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        foreach (var permission in RealtimeGroups.NotifiedPermissions.Where(held.Contains))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Permission(permission));
        }

        _logger.LogInformation("SignalR client connected. ConnectionId={ConnectionId} User={User}", Context.ConnectionId, user?.FindFirst("username")?.Value ?? "anonymous");
        await base.OnConnectedAsync();
    }
}
