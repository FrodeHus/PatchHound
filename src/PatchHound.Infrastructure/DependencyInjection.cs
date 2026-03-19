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
using PatchHound.Infrastructure.Tenants;
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
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IRemediationTaskRepository, RemediationTaskRepository>();
        services.AddScoped<IRiskAcceptanceRepository, RiskAcceptanceRepository>();
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
        services.AddScoped<IRepository<TenantVulnerability>, RepositoryBase<TenantVulnerability>>();

        // Application services
        services.AddScoped<VulnerabilityService>();
        services.AddScoped<RemediationTaskService>();
        services.AddScoped<AssetService>();
        services.AddScoped<RiskAcceptanceService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<UserService>();
        services.AddScoped<TeamService>();
        services.AddScoped<SlaService>();
        services.AddScoped<RemediationTaskProjectionService>();
        services.AddScoped<SoftwareVulnerabilityMatchService>();
        services.AddScoped<NormalizedSoftwareResolver>();
        services.AddScoped<NormalizedSoftwareProjectionService>();
        services.AddScoped<EnrichmentJobEnqueuer>();
        services.AddScoped<StagedVulnerabilityMergeService>();
        services.AddScoped<StagedAssetMergeService>();
        services.AddScoped<IngestionStateCache>();
        services.AddScoped<TenantSnapshotResolver>();
        services.AddScoped<IEnrichmentSourceRunner, NvdVulnerabilityEnrichmentRunner>();
        services.AddScoped<AiReportService>();
        services.AddScoped<TenantAiTextGenerationService>();
        services.AddScoped<SoftwareDescriptionGenerationService>();
        services.AddScoped<SoftwareDescriptionJobService>();
        services.AddScoped<IRiskChangeBriefAiSummaryService, RiskChangeBriefAiSummaryService>();
        services.AddScoped<ITenantAiConfigurationResolver, TenantAiConfigurationResolver>();
        services
            .AddHttpClient<ITenantAiResearchService, TenantAiResearchService>()
            .AddExternalHttpPolicies(maxConnectionsPerServer: 2);
        services.AddScoped<ISetupService, SetupService>();
        services.AddScoped<EnvironmentalSeverityCalculator>();
        services.AddScoped<VulnerabilityAssessmentService>();
        services.AddScoped<SecureScoreService>();
        services.AddScoped<AuditLogWriter>();

        // Notifications & Email
        services.AddScoped<INotificationService, EmailNotificationService>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
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
        services
            .AddHttpClient<DefenderApiClient>()
            .AddDefenderHttpPolicies();
        services.AddHttpClient<NvdApiClient>().AddExternalHttpPolicies(maxConnectionsPerServer: 1);
        services
            .AddHttpClient<ISecretStore, OpenBaoSecretStore>()
            .AddExternalHttpPolicies(maxConnectionsPerServer: 4);
        services.AddScoped<DefenderTenantConfigurationProvider>();
        services.AddScoped<NvdGlobalConfigurationProvider>();
        services
            .AddOptions<OpenBaoOptions>()
            .Bind(configuration.GetSection(OpenBaoOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Asset Rules
        services.AddScoped<AssetRuleFilterBuilder>();
        services.AddScoped<IAssetRuleEvaluationService, AssetRuleEvaluationService>();

        // Ingestion
        services.AddScoped<IngestionService>();

        // Event Pusher (pushes events to TanStack Start SSE endpoint)
        services.AddHttpClient<IEventPusher, HttpEventPusher>();

        return services;
    }
}
