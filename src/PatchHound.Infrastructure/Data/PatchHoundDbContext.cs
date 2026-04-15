using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Common;

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
    public DbSet<SourceSystem> SourceSystems => Set<SourceSystem>();
    public DbSet<SoftwareProduct> SoftwareProducts => Set<SoftwareProduct>();
    public DbSet<SoftwareAlias> SoftwareAliases => Set<SoftwareAlias>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<InstalledSoftware> InstalledSoftware => Set<InstalledSoftware>();
    public DbSet<TenantSoftwareProductInsight> TenantSoftwareProductInsights => Set<TenantSoftwareProductInsight>();
    public DbSet<DeviceBusinessLabel> DeviceBusinessLabels => Set<DeviceBusinessLabel>();
    public DbSet<DeviceTag> DeviceTags => Set<DeviceTag>();
    public DbSet<DeviceRule> DeviceRules => Set<DeviceRule>();
    public DbSet<DeviceRiskScore> DeviceRiskScores => Set<DeviceRiskScore>();
    public DbSet<SecurityProfile> SecurityProfiles => Set<SecurityProfile>();
    public DbSet<TenantSourceConfiguration> TenantSourceConfigurations =>
        Set<TenantSourceConfiguration>();
    public DbSet<IngestionRun> IngestionRuns => Set<IngestionRun>();
    public DbSet<IngestionSnapshot> IngestionSnapshots => Set<IngestionSnapshot>();
    public DbSet<IngestionCheckpoint> IngestionCheckpoints => Set<IngestionCheckpoint>();
    public DbSet<StagedVulnerability> StagedVulnerabilities => Set<StagedVulnerability>();
    public DbSet<StagedVulnerabilityExposure> StagedVulnerabilityExposures =>
        Set<StagedVulnerabilityExposure>();
    public DbSet<StagedDevice> StagedDevices => Set<StagedDevice>();
    public DbSet<StagedDeviceSoftwareInstallation> StagedDeviceSoftwareInstallations =>
        Set<StagedDeviceSoftwareInstallation>();
    public DbSet<EnrichmentSourceConfiguration> EnrichmentSourceConfigurations =>
        Set<EnrichmentSourceConfiguration>();
    public DbSet<EnrichmentJob> EnrichmentJobs => Set<EnrichmentJob>();
    public DbSet<EnrichmentRun> EnrichmentRuns => Set<EnrichmentRun>();
    public DbSet<TenantSlaConfiguration> TenantSlaConfigurations => Set<TenantSlaConfiguration>();
    public DbSet<TenantAiProfile> TenantAiProfiles => Set<TenantAiProfile>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserTenantRole> UserTenantRoles => Set<UserTenantRole>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<TeamMembershipRule> TeamMembershipRules => Set<TeamMembershipRule>();
    public DbSet<BusinessLabel> BusinessLabels => Set<BusinessLabel>();
    public DbSet<SoftwareCpeBinding> SoftwareCpeBindings => Set<SoftwareCpeBinding>();
    public DbSet<SoftwareTenantRecord> SoftwareTenantRecords => Set<SoftwareTenantRecord>();
    public DbSet<SoftwareProductAlias> SoftwareProductAliases =>
        Set<SoftwareProductAlias>();
    public DbSet<SoftwareProductInstallation> SoftwareProductInstallations =>
        Set<SoftwareProductInstallation>();
    public DbSet<DeviceSoftwareInstallation> DeviceSoftwareInstallations =>
        Set<DeviceSoftwareInstallation>();
    public DbSet<DeviceSoftwareInstallationEpisode> DeviceSoftwareInstallationEpisodes =>
        Set<DeviceSoftwareInstallationEpisode>();
    public DbSet<Vulnerability> Vulnerabilities => Set<Vulnerability>();
    public DbSet<VulnerabilityReference> VulnerabilityReferences => Set<VulnerabilityReference>();
    public DbSet<VulnerabilityApplicability> VulnerabilityApplicabilities => Set<VulnerabilityApplicability>();
    public DbSet<ThreatAssessment> ThreatAssessments => Set<ThreatAssessment>();
    public DbSet<DeviceVulnerabilityExposure> DeviceVulnerabilityExposures => Set<DeviceVulnerabilityExposure>();
    public DbSet<ExposureEpisode> ExposureEpisodes => Set<ExposureEpisode>();
    public DbSet<ExposureAssessment> ExposureAssessments => Set<ExposureAssessment>();
    public DbSet<OrganizationalSeverity> OrganizationalSeverities => Set<OrganizationalSeverity>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<AdvancedTool> AdvancedTools => Set<AdvancedTool>();
    public DbSet<ConnectionProfile> ConnectionProfiles => Set<ConnectionProfile>();
    public DbSet<ScanRunner> ScanRunners => Set<ScanRunner>();
    public DbSet<ScanningTool> ScanningTools => Set<ScanningTool>();
    public DbSet<ScanningToolVersion> ScanningToolVersions => Set<ScanningToolVersion>();
    public DbSet<ScanProfile> ScanProfiles => Set<ScanProfile>();
    public DbSet<ScanProfileTool> ScanProfileTools => Set<ScanProfileTool>();
    public DbSet<DeviceScanProfileAssignment> DeviceScanProfileAssignments => Set<DeviceScanProfileAssignment>();
    public DbSet<AuthenticatedScanRun> AuthenticatedScanRuns => Set<AuthenticatedScanRun>();
    public DbSet<ScanJob> ScanJobs => Set<ScanJob>();
    public DbSet<ScanJobResult> ScanJobResults => Set<ScanJobResult>();
    public DbSet<ScanJobValidationIssue> ScanJobValidationIssues => Set<ScanJobValidationIssue>();
    public DbSet<StagedDetectedSoftware> StagedDetectedSoftware => Set<StagedDetectedSoftware>();
    public DbSet<RiskAcceptance> RiskAcceptances => Set<RiskAcceptance>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AIReport> AIReports => Set<AIReport>();
    public DbSet<SoftwareDescriptionJob> SoftwareDescriptionJobs => Set<SoftwareDescriptionJob>();
    public DbSet<RemediationAiJob> RemediationAiJobs => Set<RemediationAiJob>();
    public DbSet<DeviceGroupRiskScore> DeviceGroupRiskScores => Set<DeviceGroupRiskScore>();
    public DbSet<SoftwareRiskScore> SoftwareRiskScores => Set<SoftwareRiskScore>();
    public DbSet<TeamRiskScore> TeamRiskScores => Set<TeamRiskScore>();
    public DbSet<TenantRiskScoreSnapshot> TenantRiskScoreSnapshots => Set<TenantRiskScoreSnapshot>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<WorkflowNodeExecution> WorkflowNodeExecutions => Set<WorkflowNodeExecution>();
    public DbSet<WorkflowAction> WorkflowActions => Set<WorkflowAction>();
    public DbSet<RemediationCase> RemediationCases => Set<RemediationCase>();
    public DbSet<RemediationDecision> RemediationDecisions => Set<RemediationDecision>();
    public DbSet<RemediationDecisionVulnerabilityOverride> RemediationDecisionVulnerabilityOverrides =>
        Set<RemediationDecisionVulnerabilityOverride>();
    public DbSet<AnalystRecommendation> AnalystRecommendations => Set<AnalystRecommendation>();
    public DbSet<PatchingTask> PatchingTasks => Set<PatchingTask>();
    public DbSet<ApprovalTask> ApprovalTasks => Set<ApprovalTask>();
    public DbSet<ApprovalTaskVisibleRole> ApprovalTaskVisibleRoles => Set<ApprovalTaskVisibleRole>();
    public DbSet<RemediationWorkflow> RemediationWorkflows => Set<RemediationWorkflow>();
    public DbSet<RemediationWorkflowStageRecord> RemediationWorkflowStageRecords => Set<RemediationWorkflowStageRecord>();
    public DbSet<SentinelConnectorConfiguration> SentinelConnectorConfigurations =>
        Set<SentinelConnectorConfiguration>();
    public DbSet<FeatureFlagOverride> FeatureFlagOverrides => Set<FeatureFlagOverride>();

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

    // Local override: when set to true, bypasses tenant query filters for the
    // current db context instance. Used by background enrichment runners that
    // write global (non-tenant) canonical entities under system context.
    private bool? _systemContextOverride;

    /// <summary>
    /// Temporarily override the system-context flag for this db context instance.
    /// Call <c>SetSystemContext(true)</c> before writing global entities, and
    /// <c>SetSystemContext(false)</c> (or dispose) when done.
    /// </summary>
    public void SetSystemContext(bool value) => _systemContextOverride = value;

    private bool IsSystemContext =>
        _systemContextOverride
        ?? _serviceProvider.GetService<ITenantContext>()?.IsSystemContext
        ?? false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PatchHoundDbContext).Assembly);

        // Global query filters for tenant isolation.
        // Referencing the instance property ensures EF Core re-evaluates per query.
        modelBuilder
            .Entity<Device>()
            .HasQueryFilter(e =>
                IsSystemContext
                || (AccessibleTenantIds.Contains(e.TenantId) && e.ActiveInTenant));
        modelBuilder
            .Entity<InstalledSoftware>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<TenantSoftwareProductInsight>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<DeviceBusinessLabel>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<DeviceTag>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<DeviceRule>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<DeviceRiskScore>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<SecurityProfile>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<DeviceVulnerabilityExposure>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<ExposureEpisode>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<ExposureAssessment>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<TeamMembershipRule>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<SoftwareTenantRecord>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<SoftwareProductInstallation>()
            .HasQueryFilter(e =>
                IsSystemContext
                || (AccessibleTenantIds.Contains(e.TenantId) && e.DeviceAsset.ActiveInTenant)
            );
        modelBuilder
            .Entity<DeviceSoftwareInstallation>()
            .HasQueryFilter(e =>
                IsSystemContext
                || (AccessibleTenantIds.Contains(e.TenantId) && e.DeviceAsset.ActiveInTenant)
            );
        modelBuilder
            .Entity<DeviceSoftwareInstallationEpisode>()
            .HasQueryFilter(e =>
                IsSystemContext
                || (AccessibleTenantIds.Contains(e.TenantId) && e.DeviceAsset.ActiveInTenant)
            );
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
            .Entity<SoftwareDescriptionJob>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<RemediationAiJob>()
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
            .Entity<TenantAiProfile>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
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
            .Entity<IngestionSnapshot>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<IngestionCheckpoint>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<StagedVulnerability>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<StagedVulnerabilityExposure>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<StagedDeviceSoftwareInstallation>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<DeviceGroupRiskScore>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<SoftwareRiskScore>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<TeamRiskScore>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<EnrichmentJob>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<TenantSlaConfiguration>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<TenantRiskScoreSnapshot>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));

        // Workflow entities – TenantId is nullable (system workflows have null).
        // System-scoped definitions are visible to all; tenant-scoped ones are filtered.
        modelBuilder
            .Entity<WorkflowDefinition>()
            .HasQueryFilter(e =>
                IsSystemContext
                || e.TenantId == null
                || AccessibleTenantIds.Contains(e.TenantId.Value)
            );
        modelBuilder
            .Entity<WorkflowInstance>()
            .HasQueryFilter(e =>
                IsSystemContext
                || e.TenantId == null
                || AccessibleTenantIds.Contains(e.TenantId.Value)
            );
        modelBuilder
            .Entity<WorkflowNodeExecution>()
            .HasQueryFilter(e =>
                IsSystemContext
                || e.WorkflowInstance.TenantId == null
                || AccessibleTenantIds.Contains(e.WorkflowInstance.TenantId.Value)
            );
        modelBuilder
            .Entity<WorkflowAction>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<RemediationCase>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<RemediationDecision>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<RemediationDecisionVulnerabilityOverride>()
            .HasQueryFilter(e =>
                IsSystemContext
                || AccessibleTenantIds.Contains(e.RemediationDecision.TenantId));
        modelBuilder
            .Entity<AnalystRecommendation>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<PatchingTask>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<ApprovalTask>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<ApprovalTaskVisibleRole>()
            .HasQueryFilter(e =>
                IsSystemContext
                || AccessibleTenantIds.Contains(e.ApprovalTask.TenantId));
        modelBuilder
            .Entity<RemediationWorkflow>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
        modelBuilder
            .Entity<RemediationWorkflowStageRecord>()
            .HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId));
    }
}
