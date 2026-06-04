using System.Threading.RateLimiting;
using Microsoft.FeatureManagement;
using PatchHound.Api.Hubs;
using PatchHound.Api.RateLimiting;
using PatchHound.Api.Services;
using PatchHound.Api.Workers;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Startup;

public static class PatchHoundApiServiceExtensions
{
    public static IServiceCollection AddPatchHoundApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPatchHoundInfrastructure(configuration);

        services.AddScoped<ITenantContext, Auth.TenantContext>();
        services.AddSingleton<IApiClock, SystemApiClock>();
        services.AddScoped<ApiRequestContext>();
        services.AddScoped<TenantSoftwareAliasResolver>();
        services.AddScoped<DashboardQueryService>();
        services.AddScoped<VulnerabilityDetailQueryService>();
        services.AddScoped<DeviceDetailQueryService>();
        services.AddScoped<DeviceRuleDefinitionService>();
        services.AddScoped<DeviceRulePreviewService>();
        services.AddScoped<DeviceRuleCleanupService>();
        services.AddScoped<RemediationDecisionQueryService>();
        services.AddScoped<ThreatIntelGenerationService>();
        services.AddScoped<AiRecommendationDraftService>();
        services.AddScoped<RemediationWorkflowAuthorizationService>();
        services.AddScoped<BlockedTenantAccessLogger>();
        services.AddScoped<ApprovalTaskQueryService>();
        services.AddScoped<RemediationTaskQueryService>();
        services.AddScoped<MyTasksQueryService>();
        services.AddHttpContextAccessor();

        services.AddHostedService<TenantDeletionWorker>();

        services.AddSignalR();
        services.AddScoped<IRealTimeNotifier, SignalRNotifier<NotificationHub>>();

        return services;
    }

    public static IServiceCollection AddPatchHoundRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var partitionKey = ApiRateLimitingPolicy.GetPartitionKey(context);

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => ApiRateLimitingPolicy.CreateFixedWindowOptions()
                );
            });
            options.RejectionStatusCode = 429;
        });

        return services;
    }

    public static IServiceCollection AddPatchHoundFrontendCors(
        this IServiceCollection services,
        string? frontendOrigin)
    {
        if (!string.IsNullOrWhiteSpace(frontendOrigin))
        {
            services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                    policy.WithOrigins(frontendOrigin).AllowAnyHeader().AllowAnyMethod().AllowCredentials()
                );
            });
        }

        return services;
    }

    public static IServiceCollection AddPatchHoundFeatureManagement(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IFeatureDefinitionProvider,
            Infrastructure.FeatureFlags.DatabaseFeatureDefinitionProvider>();
        services.AddFeatureManagement(configuration.GetSection("FeatureManagement"));

        return services;
    }
}
