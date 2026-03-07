namespace PatchHound.Core.Interfaces;

public interface IRealTimeNotifier
{
    Task NotifyNewVulnerabilityAsync(Guid tenantId, Guid vulnerabilityId, CancellationToken ct);
    Task NotifyTaskAssignedAsync(Guid userId, Guid taskId, CancellationToken ct);
    Task NotifyTaskStatusChangedAsync(Guid tenantId, Guid taskId, CancellationToken ct);
    Task NotifySlaWarningAsync(Guid userId, Guid taskId, CancellationToken ct);
}
