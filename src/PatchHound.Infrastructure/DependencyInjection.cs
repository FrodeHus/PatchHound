using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.AiProviders;
using PatchHound.Infrastructure.Data;
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
        services.AddDbContext<PatchHoundDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("PatchHound"))
        );

        // Unit of Work
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PatchHoundDbContext>());

        // Repositories
        services.AddScoped<IVulnerabilityRepository, VulnerabilityRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IRemediationTaskRepository, RemediationTaskRepository>();
        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<IRiskAcceptanceRepository, RiskAcceptanceRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IRepository<OrganizationalSeverity>, RepositoryBase<OrganizationalSeverity>>();

        // Application services
        services.AddScoped<VulnerabilityService>();
        services.AddScoped<RemediationTaskService>();
        services.AddScoped<AssetService>();
        services.AddScoped<CampaignService>();
        services.AddScoped<RiskAcceptanceService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<UserService>();
        services.AddScoped<TeamService>();
        services.AddScoped<AiReportService>();
        services.AddScoped<ISetupService, SetupService>();
        services.AddScoped<EnvironmentalSeverityCalculator>();
        services.AddScoped<VulnerabilityAssessmentService>();

        // Notifications & Email
        services.AddScoped<INotificationService, EmailNotificationService>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.Configure<SmtpOptions>(configuration.GetSection("Smtp"));

        // AI Report Providers
        services.AddScoped<IAiReportProvider, AzureOpenAiProvider>();
        services.AddScoped<IAiReportProvider, AnthropicProvider>();
        services.Configure<AiProviderOptions>(configuration.GetSection("AiProvider"));

        // Vulnerability Sources
        services.AddScoped<IVulnerabilitySource, DefenderVulnerabilitySource>();
        services.AddHttpClient<DefenderApiClient>();
        services.AddHttpClient<ISecretStore, OpenBaoSecretStore>();
        services.AddScoped<DefenderTenantConfigurationProvider>();
        services.Configure<OpenBaoOptions>(configuration.GetSection(OpenBaoOptions.SectionName));

        // Ingestion
        services.AddScoped<IngestionService>();

        // Event Pusher (pushes events to TanStack Start SSE endpoint)
        services.AddHttpClient<IEventPusher, HttpEventPusher>();

        return services;
    }
}
