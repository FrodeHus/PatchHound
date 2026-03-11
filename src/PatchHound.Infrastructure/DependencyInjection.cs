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
        // Database
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<PatchHoundDbContext>(
            (sp, options) =>
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
                    .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>())
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
        services.AddScoped<IEnrichmentSourceRunner, NvdVulnerabilityEnrichmentRunner>();
        services.AddScoped<AiReportService>();
        services.AddScoped<TenantAiTextGenerationService>();
        services.AddScoped<ITenantAiConfigurationResolver, TenantAiConfigurationResolver>();
        services.AddScoped<ISetupService, SetupService>();
        services.AddScoped<EnvironmentalSeverityCalculator>();
        services.AddScoped<VulnerabilityAssessmentService>();
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
            .AddExternalHttpPolicies(maxConnectionsPerServer: 4);
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

        // Ingestion
        services.AddScoped<IngestionService>();

        // Event Pusher (pushes events to TanStack Start SSE endpoint)
        services.AddHttpClient<IEventPusher, HttpEventPusher>();

        return services;
    }
}
