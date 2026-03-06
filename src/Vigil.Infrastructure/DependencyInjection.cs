using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vigil.Core.Interfaces;
using Vigil.Core.Services;
using Vigil.Infrastructure.AiProviders;
using Vigil.Infrastructure.Data;
using Vigil.Infrastructure.Options;
using Vigil.Infrastructure.Repositories;
using Vigil.Infrastructure.Services;
using Vigil.Infrastructure.VulnerabilitySources;

namespace Vigil.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddVigilInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        // Database
        services.AddDbContext<VigilDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Vigil"))
        );

        // Unit of Work
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<VigilDbContext>());

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
        services.Configure<DefenderOptions>(configuration.GetSection(DefenderOptions.SectionName));

        // Ingestion
        services.AddScoped<IngestionService>();

        return services;
    }
}
