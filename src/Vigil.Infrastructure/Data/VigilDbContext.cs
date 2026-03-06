using Microsoft.EntityFrameworkCore;
using Vigil.Core.Entities;
using Vigil.Core.Interfaces;

namespace Vigil.Infrastructure.Data;

public class VigilDbContext : DbContext, IUnitOfWork
{
    private readonly ITenantContext _tenantContext;

    public VigilDbContext(DbContextOptions<VigilDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserTenantRole> UserTenantRoles => Set<UserTenantRole>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Vulnerability> Vulnerabilities => Set<Vulnerability>();
    public DbSet<VulnerabilityAsset> VulnerabilityAssets => Set<VulnerabilityAsset>();
    public DbSet<OrganizationalSeverity> OrganizationalSeverities => Set<OrganizationalSeverity>();
    public DbSet<RemediationTask> RemediationTasks => Set<RemediationTask>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignVulnerability> CampaignVulnerabilities => Set<CampaignVulnerability>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<RiskAcceptance> RiskAcceptances => Set<RiskAcceptance>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AIReport> AIReports => Set<AIReport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VigilDbContext).Assembly);

        // Global query filters for tenant isolation
        var accessibleTenants = _tenantContext.AccessibleTenantIds;

        modelBuilder.Entity<Asset>().HasQueryFilter(e => accessibleTenants.Contains(e.TenantId));
        modelBuilder
            .Entity<Vulnerability>()
            .HasQueryFilter(e => accessibleTenants.Contains(e.TenantId));
        modelBuilder
            .Entity<RemediationTask>()
            .HasQueryFilter(e => accessibleTenants.Contains(e.TenantId));
        modelBuilder.Entity<Campaign>().HasQueryFilter(e => accessibleTenants.Contains(e.TenantId));
        modelBuilder.Entity<Comment>().HasQueryFilter(e => accessibleTenants.Contains(e.TenantId));
        modelBuilder
            .Entity<RiskAcceptance>()
            .HasQueryFilter(e => accessibleTenants.Contains(e.TenantId));
        modelBuilder
            .Entity<AuditLogEntry>()
            .HasQueryFilter(e => accessibleTenants.Contains(e.TenantId));
        modelBuilder
            .Entity<Notification>()
            .HasQueryFilter(e => accessibleTenants.Contains(e.TenantId));
        modelBuilder
            .Entity<OrganizationalSeverity>()
            .HasQueryFilter(e => accessibleTenants.Contains(e.TenantId));
        modelBuilder.Entity<AIReport>().HasQueryFilter(e => accessibleTenants.Contains(e.TenantId));
        modelBuilder.Entity<Team>().HasQueryFilter(e => accessibleTenants.Contains(e.TenantId));
    }
}
