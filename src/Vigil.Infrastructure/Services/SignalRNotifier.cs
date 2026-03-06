using Microsoft.AspNetCore.SignalR;
using Vigil.Core.Interfaces;

namespace Vigil.Infrastructure.Services;

public class SignalRNotifier<THub> : IRealTimeNotifier
    where THub : Hub
{
    private readonly IHubContext<THub> _hubContext;

    public SignalRNotifier(IHubContext<THub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyNewVulnerabilityAsync(
        Guid tenantId,
        Guid vulnerabilityId,
        CancellationToken ct
    )
    {
        await _hubContext
            .Clients.Group($"tenant-{tenantId}")
            .SendAsync("NewVulnerability", new { tenantId, vulnerabilityId }, ct);
    }

    public async Task NotifyTaskAssignedAsync(Guid userId, Guid taskId, CancellationToken ct)
    {
        await _hubContext
            .Clients.Group($"user-{userId}")
            .SendAsync("TaskAssigned", new { userId, taskId }, ct);
    }

    public async Task NotifyTaskStatusChangedAsync(Guid tenantId, Guid taskId, CancellationToken ct)
    {
        await _hubContext
            .Clients.Group($"tenant-{tenantId}")
            .SendAsync("TaskStatusChanged", new { tenantId, taskId }, ct);
    }

    public async Task NotifySlaWarningAsync(Guid userId, Guid taskId, CancellationToken ct)
    {
        await _hubContext
            .Clients.Group($"user-{userId}")
            .SendAsync("SLAWarning", new { userId, taskId }, ct);
    }
}
