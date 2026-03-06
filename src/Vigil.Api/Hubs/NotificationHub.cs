using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Vigil.Core.Interfaces;

namespace Vigil.Api.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    private readonly ITenantContext _tenantContext;

    public NotificationHub(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public override async Task OnConnectedAsync()
    {
        // Add user to tenant groups for broadcast
        foreach (var tenantId in _tenantContext.AccessibleTenantIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant-{tenantId}");
        }

        // Add user to their own user group for direct notifications
        var userId = _tenantContext.CurrentUserId;
        if (userId != Guid.Empty)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // SignalR automatically removes connections from groups on disconnect
        await base.OnDisconnectedAsync(exception);
    }
}
