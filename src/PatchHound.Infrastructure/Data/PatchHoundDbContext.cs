using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;

namespace PatchHound.Infrastructure.Data;

public class PatchHoundDbContext : DbContext, IUnitOfWork
{
    private readonly IServiceProvider _serviceProvider;

    public PatchHoundDbContext(
        DbContextOptions<PatchHoundDbContext> options,
        IServiceProvider serviceProvider
    )
        : base(options)
    {
        _serviceProvider = serviceProvider;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantSourceConfiguration> TenantSourceConfigurations =>
        Set<TenantSourceConfiguration>();
    public DbSet<IngestionRun> IngestionRuns => Set<IngestionRun>();
    public DbSet<StagedVulnerability> StagedVulnerabilities => Set<StagedVulnerability>();
    public DbSet<StagedVulnerabilityExposure> StagedVulnerabilityExposures =>
        Set<StagedVulnerabilityExposure>();
    public DbSet<StagedAsset> StagedAssets => Set<StagedAsset>();
    public DbSet<StagedDeviceSoftwareInstallation> StagedDeviceSoftwareInstallations =>
        Set<StagedDeviceSoftwareInstallation>();
    public DbSet<EnrichmentSourceConfiguration> EnrichmentSourceConfigurations =>
        Set<EnrichmentSourceConfiguration>();
    public DbSet<EnrichmentJob> EnrichmentJobs => Set<EnrichmentJob>();
    public DbSet<EnrichmentRun> EnrichmentRuns => Set<EnrichmentRun>();
    public DbSet<TenantSlaConfiguration> TenantSlaConfigurations => Set<TenantSlaConfiguration>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserTenantRole> UserTenantRoles => Set<UserTenantRole>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetSecurityProfile> AssetSecurityProfiles => Set<AssetSecurityProfile>();
    public DbSet<SoftwareCpeBinding> SoftwareCpeBindings => Set<SoftwareCpeBinding>();
    public DbSet<SoftwareVulnerabilityMatch> SoftwareVulnerabilityMatches =>
        Set<SoftwareVulnerabilityMatch>();
    public DbSet<NormalizedSoftware> NormalizedSoftware => Set<NormalizedSoftware>();
    public DbSet<NormalizedSoftwareAlias> NormalizedSoftwareAliases =>
        Set<NormalizedSoftwareAlias>();
    public DbSet<NormalizedSoftwareInstallation> NormalizedSoftwareInstallations =>
        Set<NormalizedSoftwareInstallation>();
    public DbSet<NormalizedSoftwareVulnerabilityProjection>
        NormalizedSoftwareVulnerabilityProjections =>
            Set<NormalizedSoftwareVulnerabilityProjection>();
    public DbSet<DeviceSoftwareInstallation> DeviceSoftwareInstallations =>
        Set<DeviceSoftwareInstallation>();
    public DbSet<DeviceSoftwareInstallationEpisode> DeviceSoftwareInstallationEpisodes =>
        Set<DeviceSoftwareInstallationEpisode>();
    public DbSet<Vulnerability> Vulnerabilities => Set<Vulnerability>();
    public DbSet<VulnerabilityAffectedSoftware> VulnerabilityAffectedSoftware =>
        Set<VulnerabilityAffectedSoftware>();
    public DbSet<VulnerabilityReference> VulnerabilityReferences => Set<VulnerabilityReference>();
    public DbSet<VulnerabilityAsset> VulnerabilityAssets => Set<VulnerabilityAsset>();
    public DbSet<VulnerabilityAssetEpisode> VulnerabilityAssetEpisodes =>
        Set<VulnerabilityAssetEpisode>();
    public DbSet<VulnerabilityAssetAssessment> VulnerabilityAssetAssessments =>
        Set<VulnerabilityAssetAssessment>();
    public DbSet<OrganizationalSeverity> OrganizationalSeverities => Set<OrganizationalSeverity>();
    public DbSet<RemediationTask> RemediationTasks => Set<RemediationTask>();
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

    public Task ExecuteResilientAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken ct = default
    )
    {
        var strategy = Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async innerCt => await operation(innerCt), ct);
    }

    private sealed class EfTransaction(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction inner
    ) : IUnitOfWorkTransaction
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
    private bool IsSystemContext =>
        _serviceProvider.GetService<ITenantContext>()?.IsSystemContext ?? false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PatchHoundDbContext).Assembly);

        // Global query filters for tenant isolation.
        // Referencing the instance property ensures EF Core re-evaluates per query.
        modelBuilder
            .Entity<Asset>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<AssetSecurityProfile>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<SoftwareCpeBinding>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<SoftwareVulnerabilityMatch>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<NormalizedSoftware>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<NormalizedSoftwareAlias>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<NormalizedSoftwareInstallation>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<NormalizedSoftwareVulnerabilityProjection>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<DeviceSoftwareInstallation>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<DeviceSoftwareInstallationEpisode>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<Vulnerability>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<VulnerabilityAffectedSoftware>()
            .HasQueryFilter(e =>
                IsSystemContext || AccessibleTenantIds.Contains(e.Vulnerability.TenantId)
            );
        modelBuilder
            .Entity<VulnerabilityReference>()
            .HasQueryFilter(e =>
                IsSystemContext || AccessibleTenantIds.Contains(e.Vulnerability.TenantId)
            );
        modelBuilder
            .Entity<VulnerabilityAsset>()
            .HasQueryFilter(e =>
                IsSystemContext
                || (
                    AccessibleTenantIds.Contains(e.Vulnerability.TenantId)
                    && AccessibleTenantIds.Contains(e.Asset.TenantId)
                )
            );
        modelBuilder
            .Entity<VulnerabilityAssetEpisode>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<VulnerabilityAssetAssessment>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<RemediationTask>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<Comment>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<RiskAcceptance>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<AuditLogEntry>()
            .HasQueryFilter(e =>
                IsSystemContext
                || e.TenantId == Guid.Empty
                || AccessibleTenantIds.Contains(e.TenantId)
            );
        modelBuilder
            .Entity<Notification>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<OrganizationalSeverity>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<AIReport>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<Team>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<TeamMember>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.Team.TenantId));
        modelBuilder
            .Entity<Tenant>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.Id));
        modelBuilder
            .Entity<UserTenantRole>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<TenantSourceConfiguration>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<IngestionRun>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<StagedVulnerability>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<StagedVulnerabilityExposure>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<StagedAsset>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<StagedDeviceSoftwareInstallation>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<EnrichmentJob>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<TenantSlaConfiguration>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
    }
}
