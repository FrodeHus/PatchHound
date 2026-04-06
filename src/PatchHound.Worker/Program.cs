using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Core.Enums;
using PatchHound.Api.Services;
using PatchHound.Infrastructure;
using PatchHound.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPatchHoundInfrastructure(builder.Configuration);

// Tenant context for worker (system-level, all tenants accessible)
builder.Services.AddScoped<ITenantContext, WorkerTenantContext>();
builder.Services.AddScoped<IEmailSender, NoOpEmailSender>();
builder.Services.AddScoped<IRealTimeNotifier, NoOpRealTimeNotifier>();
builder.Services.AddScoped<RemediationDecisionQueryService>();

// Hosted services
builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHostedService<EnrichmentWorker>();
builder.Services.AddHostedService<SoftwareDescriptionWorker>();
builder.Services.AddHostedService<RemediationAiWorker>();
builder.Services.AddHostedService<SlaCheckWorker>();
builder.Services.AddHostedService<WorkflowWorker>();
builder.Services.AddHostedService<ApprovalExpiryWorker>();
builder.Services.AddHostedService<ScanSchedulerWorker>();

var host = builder.Build();

Console.WriteLine("[startup] PatchHound.Worker host configured, starting background services");
await host.RunAsync();

/// <summary>
/// Tenant context for the worker process. Provides system-level access to all tenants.
/// </summary>
internal class WorkerTenantContext : ITenantContext
{
    public Guid? CurrentTenantId => null;
    public IReadOnlyList<Guid> AccessibleTenantIds => Array.Empty<Guid>();

    public Guid CurrentUserId => Guid.Empty;
    public bool IsSystemContext => true;
    public bool IsInternalUser => true;
    public UserAccessScope CurrentAccessScope => UserAccessScope.Internal;

    public bool HasAccessToTenant(Guid tenantId) => true;

    public IReadOnlyList<string> GetRolesForTenant(Guid tenantId) => Array.Empty<string>();
}

/// <summary>
/// No-op email sender for worker process. Replace with real implementation when needed.
/// </summary>
internal class NoOpEmailSender : IEmailSender
{
    public Task SendEmailAsync(
        string to,
        string subject,
        string htmlBody,
        CancellationToken ct = default
    ) => Task.CompletedTask;
}

/// <summary>
/// No-op real-time notifier for worker process. SignalR is only available in the API.
/// </summary>
internal class NoOpRealTimeNotifier : IRealTimeNotifier
{
    public Task NotifyNewVulnerabilityAsync(Guid tenantId, Guid vulnerabilityId, CancellationToken ct) => Task.CompletedTask;
    public Task NotifyTaskAssignedAsync(Guid userId, Guid taskId, CancellationToken ct) => Task.CompletedTask;
    public Task NotifyTaskStatusChangedAsync(Guid tenantId, Guid taskId, CancellationToken ct) => Task.CompletedTask;
    public Task NotifySlaWarningAsync(Guid userId, Guid taskId, CancellationToken ct) => Task.CompletedTask;
    public Task NotifyApprovalTaskCreatedAsync(Guid tenantId, Guid approvalTaskId, CancellationToken ct) => Task.CompletedTask;
}
