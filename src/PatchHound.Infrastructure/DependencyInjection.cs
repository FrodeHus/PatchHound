using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.AiProviders;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.ExternalHttp;
using PatchHound.Infrastructure.Options;
using PatchHound.Infrastructure.Repositories;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Services.Inventory;
using PatchHound.Infrastructure.Services.Workflows;
using PatchHound.Infrastructure.Tenants;
using PatchHound.Infrastructure.CredentialSources;
using PatchHound.Infrastructure.VulnerabilitySources;

namespace PatchHound.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPatchHoundInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        // Redis (optional — ingestion cache)
        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(
                StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnectionString));
        }

        // Database
        services.AddSingleton<SentinelAuditQueue>();
        services.AddHttpClient("SentinelConnector");
        services.AddHostedService<SentinelConnectorWorker>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        void ConfigureDbContext(IServiceProvider sp, DbContextOptionsBuilder options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("PatchHound"),
                    npgsql =>
                        npgsql.EnableRetryOnFailure(
                            maxRetryCount: 5,
                            maxRetryDelay: TimeSpan.FromSeconds(10),
                            errorCodesToAdd: null
                        )
                )
                .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());

        services.AddDbContext<PatchHoundDbContext>(ConfigureDbContext);
        services.AddDbContextFactory<PatchHoundDbContext>(
            ConfigureDbContext,
            ServiceLifetime.Scoped
        );

        // Unit of Work
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PatchHoundDbContext>());

        // Repositories
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<
            IRepository<TenantSourceConfiguration>,
            RepositoryBase<TenantSourceConfiguration>
        >();
        services.AddScoped<
            IRepository<EnrichmentSourceConfiguration>,
            RepositoryBase<EnrichmentSourceConfiguration>
        >();
        services.AddScoped<
            IRepository<TenantSlaConfiguration>,
            RepositoryBase<TenantSlaConfiguration>
        >();
        services.AddScoped<IRepository<TenantAiProfile>, RepositoryBase<TenantAiProfile>>();
        services.AddScoped<
            IRepository<OrganizationalSeverity>,
            RepositoryBase<OrganizationalSeverity>
        >();

        // Application services
        services.AddScoped<DeviceService>();
        services.AddScoped<UserService>();
        services.AddScoped<TeamService>();
        services.AddScoped<TeamMembershipRuleFilterBuilder>();
        services.AddScoped<TeamMembershipRuleService>();
        services.AddScoped<SlaService>();
        services.AddScoped<RemediationCaseService>();
        services.AddScoped<RemediationWorkflowService>();
        services.AddScoped<PatchingTaskService>();
        services.AddScoped<ApprovalTaskService>();
        services.AddScoped<RemediationDecisionService>();
        services.AddScoped<AnalystRecommendationService>();
        services.AddScoped<NormalizedSoftwareResolver>();
        services.AddScoped<VulnerabilityResolver>();
        services.AddScoped<ThreatAssessmentService>();
        services.AddScoped<NormalizedSoftwareProjectionService>();
        services.AddScoped<CycloneDxSupplyChainImportService>();
        services.AddScoped<EnrichmentJobEnqueuer>();
        services.AddScoped<IngestionStateCache>();
        services.AddScoped<TenantSnapshotResolver>();
        services.AddScoped<IEnrichmentSourceRunner, NvdVulnerabilityEnrichmentRunner>();
        services.AddScoped<IEnrichmentSourceRunner, DefenderVulnerabilityEnrichmentRunner>();
        services.AddScoped<IEnrichmentSourceRunner, EndOfLifeSoftwareEnrichmentRunner>();
        services.AddScoped<IEnrichmentSourceRunner, SupplyChainEvidenceEnrichmentRunner>();
        services.AddScoped<AiReportService>();
        services.AddScoped<TenantAiTextGenerationService>();
        services.AddScoped<SoftwareDescriptionGenerationService>();
        services.AddScoped<SoftwareDescriptionJobService>();
        services.AddScoped<RemediationAiJobService>();
        services.AddScoped<IRiskChangeBriefAiSummaryService, RiskChangeBriefAiSummaryService>();
        services.AddScoped<ITenantAiConfigurationResolver, TenantAiConfigurationResolver>();
        services
            .AddHttpClient<ITenantAiResearchService, TenantAiResearchService>()
            .AddExternalHttpPolicies(maxConnectionsPerServer: 2);
        services.AddScoped<ISetupService, SetupService>();
        services.AddScoped<EnvironmentalSeverityCalculator>();
        services.AddScoped<ExposureDerivationService>();
        services.AddScoped<ExposureEpisodeService>();
        services.AddScoped<ExposureAssessmentService>();
        services.AddScoped<RiskScoreService>();
        services.AddScoped<RiskRefreshService>();
        services.AddScoped<AuditLogWriter>();
        services.AddScoped<AdvancedToolExecutionService>();
        services.AddScoped<PatchHound.Infrastructure.AuthenticatedScans.ConnectionProfileSecretWriter>();
        services.AddScoped<PatchHound.Infrastructure.AuthenticatedScans.AuthenticatedScanOutputValidator>();
        services.AddScoped<PatchHound.Infrastructure.AuthenticatedScans.AuthenticatedScanIngestionService>();
        services.AddScoped<PatchHound.Infrastructure.AuthenticatedScans.ScanJobDispatcher>();
        services.AddScoped<PatchHound.Infrastructure.AuthenticatedScans.ScanRunCompletionService>();
        services.AddScoped<PatchHound.Infrastructure.AuthenticatedScans.ScanSchedulerTickHandler>();
        services.AddScoped<PatchHound.Infrastructure.AuthenticatedScans.ScanningToolVersionStore>();
        services.AddScoped<NotificationEmailConfigurationResolver>();
        services.AddHostedService<DefaultTeamSeedHostedService>();
        services.AddHostedService<SourceSystemSeedHostedService>();

        // Notifications & Email
        services.AddScoped<INotificationService, EmailNotificationService>();
        services.AddScoped<SmtpEmailSender>();
        services.AddScoped<IEmailSender, ConfigurableEmailSender>();
        services.Configure<SmtpOptions>(configuration.GetSection("Smtp"));

        // AI Report Providers
        services
            .AddHttpClient<OllamaAiProvider>()
            .AddExternalHttpPolicies(maxConnectionsPerServer: 4);
        services
            .AddHttpClient<AzureOpenAiProvider>()
            .AddExternalHttpPolicies(maxConnectionsPerServer: 4);
        services
            .AddHttpClient<OpenAiProvider>()
            .AddExternalHttpPolicies(maxConnectionsPerServer: 4);
        services.AddScoped<IAiReportProvider>(sp => sp.GetRequiredService<OllamaAiProvider>());
        services.AddScoped<IAiReportProvider>(sp => sp.GetRequiredService<AzureOpenAiProvider>());
        services.AddScoped<IAiReportProvider>(sp => sp.GetRequiredService<OpenAiProvider>());

        // Vulnerability Sources
        services.AddScoped<IVulnerabilitySource, DefenderVulnerabilitySource>();
        services.AddScoped<IVulnerabilitySource, EntraApplicationSource>();
        services.AddScoped<NvdVulnerabilitySource>();
        services
            .AddHttpClient<DefenderApiClient>()
            .AddDefenderHttpPolicies();
        services
            .AddHttpClient<MailgunEmailSender>()
            .AddExternalHttpPolicies(maxConnectionsPerServer: 2);
        services.AddHttpClient<NvdApiClient>().AddExternalHttpPolicies(maxConnectionsPerServer: 1);
        services.AddHttpClient<EndOfLifeApiClient>().AddExternalHttpPolicies(maxConnectionsPerServer: 2);
        services.AddHttpClient<SupplyChainCatalogClient>().AddExternalHttpPolicies(maxConnectionsPerServer: 2);
        services
            .AddHttpClient<ISecretStore, OpenBaoSecretStore>()
            .AddExternalHttpPolicies(maxConnectionsPerServer: 4);
        services.AddScoped<DefenderTenantConfigurationProvider>();
        services.AddScoped<EntraApplicationsConfigurationProvider>();
        services
            .AddHttpClient<EntraGraphApiClient>()
            .AddExternalHttpPolicies(maxConnectionsPerServer: 2);
        services.AddScoped<NvdGlobalConfigurationProvider>();
        services
            .AddOptions<OpenBaoOptions>()
            .Bind(configuration.GetSection(OpenBaoOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Device Rules
        services.AddScoped<DeviceRuleFilterBuilder>();
        services.AddScoped<IDeviceRuleEvaluationService, DeviceRuleEvaluationService>();

        // Inventory resolvers & staged-device merge (needed by IngestionService + AuthenticatedScanIngestionService)
        services.AddScoped<ISoftwareProductResolver, PatchHound.Infrastructure.Services.Inventory.SoftwareProductResolver>();
        services.AddScoped<IDeviceResolver, PatchHound.Infrastructure.Services.Inventory.DeviceResolver>();
        services.AddScoped<IStagedDeviceMergeService, StagedDeviceMergeService>();

        // Ingestion
        services.AddScoped<IngestionService>();

        // Event Pusher (pushes events to TanStack Start SSE endpoint)
        services.AddHttpClient<IEventPusher, HttpEventPusher>();

        // Workflow Engine
        services.AddScoped<IWorkflowEngine, WorkflowEngine>();
        services.AddScoped<IWorkflowTriggerService, WorkflowTriggerService>();
        services.AddScoped<IWorkflowNodeExecutor, StartNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, EndNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, AssignGroupNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, ConditionNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, SendNotificationNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, MergeNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, SystemTaskNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, WaitForActionNodeExecutor>();

        return services;
    }
}
