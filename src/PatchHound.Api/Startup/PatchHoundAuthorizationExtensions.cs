using Microsoft.AspNetCore.Authorization;
using PatchHound.Api.Auth;
using PatchHound.Core.Enums;

namespace PatchHound.Api.Startup;

public static class PatchHoundAuthorizationExtensions
{
    public static IServiceCollection AddPatchHoundAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                Policies.ViewVulnerabilities,
                policy => policy.AddRequirements(new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
                    RoleName.CustomerViewer,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst,
                    RoleName.AssetOwner,
                    RoleName.Stakeholder,
                    RoleName.Auditor,
                    RoleName.TechnicalManager)));

            options.AddPolicy(
                Policies.ModifyVulnerabilities,
                policy => policy.AddRequirements(new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst)));

            options.AddPolicy(
                Policies.AdjustSeverity,
                policy => policy.AddRequirements(new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst)));

            options.AddPolicy(
                Policies.AssignTasks,
                policy => policy.AddRequirements(new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst)));

            options.AddPolicy(
                Policies.UpdateTaskStatus,
                policy => policy.AddRequirements(new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst,
                    RoleName.AssetOwner)));

            options.AddPolicy(
                Policies.RequestRiskAcceptance,
                policy => policy.AddRequirements(new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst,
                    RoleName.AssetOwner)));

            options.AddPolicy(
                Policies.ApproveRiskAcceptance,
                policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.CustomerAdmin)));

            options.AddPolicy(
                Policies.ViewAuditLogs,
                policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin, RoleName.Auditor, RoleName.CustomerAdmin)));

            options.AddPolicy(
                Policies.ManageUsers,
                policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin, RoleName.CustomerAdmin)));

            options.AddPolicy(
                Policies.ViewTeams,
                policy => policy.AddRequirements(new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
                    RoleName.CustomerViewer,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst,
                    RoleName.AssetOwner,
                    RoleName.Stakeholder,
                    RoleName.Auditor,
                    RoleName.TechnicalManager)));

            options.AddPolicy(
                Policies.ViewTenants,
                policy => policy.AddRequirements(new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
                    RoleName.CustomerViewer,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst,
                    RoleName.AssetOwner,
                    RoleName.Stakeholder,
                    RoleName.Auditor,
                    RoleName.TechnicalManager)));

            options.AddPolicy(
                Policies.ConfigureTenant,
                policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin, RoleName.SecurityManager)));

            options.AddPolicy(
                Policies.GenerateAiReports,
                policy => policy.AddRequirements(new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst)));

            options.AddPolicy(
                Policies.AddComments,
                policy => policy.AddRequirements(new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst,
                    RoleName.AssetOwner)));

            options.AddPolicy(
                Policies.ManageTeams,
                policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin)));

            options.AddPolicy(
                Policies.ViewApprovalTasks,
                policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.TechnicalManager, RoleName.CustomerAdmin, RoleName.CustomerOperator)));

            options.AddPolicy(
                Policies.ResolveApprovalTask,
                policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.TechnicalManager, RoleName.CustomerAdmin, RoleName.CustomerOperator)));

            options.AddPolicy(
                Policies.ManageVault,
                policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin)));

            options.AddPolicy(
                Policies.ManageWorkflows,
                policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin, RoleName.SecurityManager)));

            options.AddPolicy(
                Policies.PerformMaintenance,
                policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin)));

            options.AddPolicy(
                Policies.ManageGlobalSettings,
                policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin)));

            options.AddPolicy(
                Policies.ManageAuthenticatedScans,
                policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin, RoleName.CustomerAdmin, RoleName.SecurityManager)));

            options.AddPolicy(
                Policies.CreateDecision,
                policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.TechnicalManager)));

            options.AddPolicy(
                Policies.ApproveDecision,
                policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.TechnicalManager)));

            options.AddPolicy(
                Policies.AddRecommendation,
                policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.SecurityAnalyst)));
        });

        services.AddScoped<IAuthorizationHandler, RoleRequirementHandler>();

        return services;
    }
}
