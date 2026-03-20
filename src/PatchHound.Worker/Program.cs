using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure;
using PatchHound.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPatchHoundInfrastructure(builder.Configuration);

// Tenant context for worker (system-level, all tenants accessible)
builder.Services.AddScoped<ITenantContext, WorkerTenantContext>();
builder.Services.AddScoped<IEmailSender, NoOpEmailSender>();

// Hosted services
builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHostedService<EnrichmentWorker>();
builder.Services.AddHostedService<SoftwareDescriptionWorker>();
builder.Services.AddHostedService<SlaCheckWorker>();
builder.Services.AddHostedService<WorkflowWorker>();

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
