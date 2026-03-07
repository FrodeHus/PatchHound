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

// Apply pending database migrations on startup
using (var scope = host.Services.CreateScope())
{
    Console.WriteLine("[startup] PatchHound.Worker starting database migration check");
    var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
    await dbContext.Database.MigrateAsync();
    Console.WriteLine("[startup] PatchHound.Worker database migration check completed");
}

Console.WriteLine("[startup] PatchHound.Worker host configured, starting background services");
await host.RunAsync();

/// <summary>
/// Tenant context for the worker process. Provides system-level access to all tenants.
/// </summary>
internal class WorkerTenantContext : ITenantContext
{
    private readonly PatchHoundDbContext _dbContext;

    public WorkerTenantContext(PatchHoundDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Guid? CurrentTenantId => null;

    public IReadOnlyList<Guid> AccessibleTenantIds =>
        _dbContext.Tenants.AsNoTracking().IgnoreQueryFilters().Select(t => t.Id).ToList();

    public Guid CurrentUserId => Guid.Empty; // System user
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
