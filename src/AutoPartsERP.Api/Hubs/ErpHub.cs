using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AutoPartsERP.Api.Hubs;

[Authorize]
public sealed class ErpHub : Hub
{
    private readonly ILogger<ErpHub> _logger;

    public ErpHub(ILogger<ErpHub> logger)
    {
        _logger = logger;
    }

    public async Task JoinGroup(string groupName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    public async Task LeaveGroup(string groupName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
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
