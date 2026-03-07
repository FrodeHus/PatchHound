using Microsoft.EntityFrameworkCore;
using PatchHound.Infrastructure;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPatchHoundInfrastructure(builder.Configuration);

// Tenant context for worker (system-level, all tenants accessible)
builder.Services.AddScoped<ITenantContext, WorkerTenantContext>();
builder.Services.AddScoped<IEmailSender, NoOpEmailSender>();

// Hosted services
builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHostedService<SlaCheckWorker>();

var host = builder.Build();

Console.WriteLine("[startup] PatchHound.Worker host configured, starting background services");
await host.RunAsync();

/// <summary>
/// Tenant context for the worker process. Provides system-level access to all tenants.
/// </summary>
internal class WorkerTenantContext : ITenantContext
{
    private readonly PatchHoundDbContext _dbContext;
    private IReadOnlyList<Guid>? _accessibleTenantIds;

    public WorkerTenantContext(PatchHoundDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Guid? CurrentTenantId => null;

    public IReadOnlyList<Guid> AccessibleTenantIds => _accessibleTenantIds ?? Array.Empty<Guid>();

    public Guid CurrentUserId => Guid.Empty;

    public bool HasAccessToTenant(Guid tenantId) => true;

    public IReadOnlyList<string> GetRolesForTenant(Guid tenantId) => Array.Empty<string>();

    internal async Task InitializeAsync(CancellationToken ct)
    {
        _accessibleTenantIds = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Select(t => t.Id)
            .ToListAsync(ct);
    }
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
