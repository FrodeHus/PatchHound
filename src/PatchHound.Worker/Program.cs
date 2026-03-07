using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Options;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.VulnerabilitySources;
using PatchHound.Worker;

var builder = Host.CreateApplicationBuilder(args);

// DbContext
builder.Services.AddDbContext<PatchHoundDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PatchHound"))
);

// Tenant context for worker (system-level, all tenants accessible)
builder.Services.AddScoped<ITenantContext, WorkerTenantContext>();

// Ingestion pipeline
builder.Services.AddScoped<IngestionService>();
builder.Services.Configure<DefenderOptions>(
    builder.Configuration.GetSection(DefenderOptions.SectionName)
);
builder.Services.AddHttpClient<DefenderApiClient>();
builder.Services.AddScoped<IVulnerabilitySource, DefenderVulnerabilitySource>();

// Notification services
builder.Services.AddScoped<INotificationService, EmailNotificationService>();
builder.Services.AddScoped<IEmailSender, NoOpEmailSender>();

// SLA service
builder.Services.AddScoped<SlaService>();

// Hosted services
builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHostedService<SlaCheckWorker>();

var host = builder.Build();

// Apply pending database migrations on startup
using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
    await dbContext.Database.MigrateAsync();
}

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
        _dbContext.Tenants.AsNoTracking().Select(t => t.Id).ToList();

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
