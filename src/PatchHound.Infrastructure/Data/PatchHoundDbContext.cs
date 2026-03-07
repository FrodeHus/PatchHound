using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;

namespace PatchHound.Infrastructure.Data;

public class PatchHoundDbContext : DbContext, IUnitOfWork
{
    private readonly IServiceProvider _serviceProvider;

    public PatchHoundDbContext(DbContextOptions<PatchHoundDbContext> options, IServiceProvider serviceProvider)
        : base(options)
    {
        _serviceProvider = serviceProvider;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserTenantRole> UserTenantRoles => Set<UserTenantRole>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetSecurityProfile> AssetSecurityProfiles => Set<AssetSecurityProfile>();
    public DbSet<DeviceSoftwareInstallation> DeviceSoftwareInstallations => Set<DeviceSoftwareInstallation>();
    public DbSet<DeviceSoftwareInstallationEpisode> DeviceSoftwareInstallationEpisodes =>
        Set<DeviceSoftwareInstallationEpisode>();
    public DbSet<Vulnerability> Vulnerabilities => Set<Vulnerability>();
    public DbSet<VulnerabilityAsset> VulnerabilityAssets => Set<VulnerabilityAsset>();
    public DbSet<VulnerabilityAssetEpisode> VulnerabilityAssetEpisodes => Set<VulnerabilityAssetEpisode>();
    public DbSet<VulnerabilityAssetAssessment> VulnerabilityAssetAssessments => Set<VulnerabilityAssetAssessment>();
    public DbSet<OrganizationalSeverity> OrganizationalSeverities => Set<OrganizationalSeverity>();
    public DbSet<RemediationTask> RemediationTasks => Set<RemediationTask>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignVulnerability> CampaignVulnerabilities => Set<CampaignVulnerability>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<RiskAcceptance> RiskAcceptances => Set<RiskAcceptance>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AIReport> AIReports => Set<AIReport>();

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        var transaction = await Database.BeginTransactionAsync(ct);
        return new EfTransaction(transaction);
    }

    private sealed class EfTransaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction inner) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => inner.CommitAsync(ct);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    // Resolved lazily to break the circular dependency:
    // PatchHoundDbContext → ITenantContext → TenantContext → PatchHoundDbContext.
    // IServiceProvider is not traced by DI cycle detection.
    // At runtime the scoped PatchHoundDbContext already exists, so resolving
    // ITenantContext doesn't cause recursive construction.
    // EF Core re-evaluates instance member references in query filters per query,
    // so each query gets the current user's tenant list.
    private IReadOnlyList<Guid> AccessibleTenantIds =>
        _serviceProvider.GetService<ITenantContext>()?.AccessibleTenantIds ?? [];

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PatchHoundDbContext).Assembly);

        // Global query filters for tenant isolation.
        // Referencing the instance property ensures EF Core re-evaluates per query.
        modelBuilder.Entity<Asset>().HasQueryFilter(e => AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<AssetSecurityProfile>()
            .HasQueryFilter(e => AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<DeviceSoftwareInstallation>()
            .HasQueryFilter(e => AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<DeviceSoftwareInstallationEpisode>()
            .HasQueryFilter(e => AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<Vulnerability>()
            .HasQueryFilter(e => AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<VulnerabilityAssetEpisode>()
            .HasQueryFilter(e => AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<VulnerabilityAssetAssessment>()
            .HasQueryFilter(e => AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<RemediationTask>()
            .HasQueryFilter(e => AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<Campaign>()
            .HasQueryFilter(e => AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<Comment>()
            .HasQueryFilter(e => AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<RiskAcceptance>()
            .HasQueryFilter(e => AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<AuditLogEntry>()
            .HasQueryFilter(e => AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<Notification>()
            .HasQueryFilter(e => AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<OrganizationalSeverity>()
            .HasQueryFilter(e => AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<AIReport>()
            .HasQueryFilter(e => AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<Team>()
            .HasQueryFilter(e => AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<Tenant>()
            .HasQueryFilter(e => AccessibleTenantIds.Contains(e.Id));
    }
}
