using Microsoft.EntityFrameworkCore;
using Vigil.Core.Interfaces;
using Vigil.Core.Services;
using Vigil.Infrastructure.Data;
using Vigil.Infrastructure.Options;
using Vigil.Infrastructure.Services;
using Vigil.Infrastructure.VulnerabilitySources;
using Vigil.Worker;

var builder = Host.CreateApplicationBuilder(args);

// DbContext
builder.Services.AddDbContext<VigilDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Vigil"))
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
host.Run();

/// <summary>
/// Tenant context for the worker process. Provides system-level access to all tenants.
/// </summary>
internal class WorkerTenantContext : ITenantContext
{
    private readonly VigilDbContext _dbContext;

    public WorkerTenantContext(VigilDbContext dbContext)
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
