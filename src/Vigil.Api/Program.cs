using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;
using Vigil.Api.Auth;
using Vigil.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Authentication - Entra ID multi-tenant
builder.Services.AddAuthentication()
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.ViewVulnerabilities, policy =>
        policy.AddRequirements(new RoleRequirement(
            RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.SecurityAnalyst,
            RoleName.AssetOwner, RoleName.Stakeholder, RoleName.Auditor)));

    options.AddPolicy(Policies.ModifyVulnerabilities, policy =>
        policy.AddRequirements(new RoleRequirement(
            RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.SecurityAnalyst)));

    options.AddPolicy(Policies.AdjustSeverity, policy =>
        policy.AddRequirements(new RoleRequirement(
            RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.SecurityAnalyst)));

    options.AddPolicy(Policies.AssignTasks, policy =>
        policy.AddRequirements(new RoleRequirement(
            RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.SecurityAnalyst)));

    options.AddPolicy(Policies.UpdateTaskStatus, policy =>
        policy.AddRequirements(new RoleRequirement(
            RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.SecurityAnalyst, RoleName.AssetOwner)));

    options.AddPolicy(Policies.RequestRiskAcceptance, policy =>
        policy.AddRequirements(new RoleRequirement(
            RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.SecurityAnalyst, RoleName.AssetOwner)));

    options.AddPolicy(Policies.ApproveRiskAcceptance, policy =>
        policy.AddRequirements(new RoleRequirement(
            RoleName.GlobalAdmin, RoleName.SecurityManager)));

    options.AddPolicy(Policies.ManageCampaigns, policy =>
        policy.AddRequirements(new RoleRequirement(
            RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.SecurityAnalyst)));

    options.AddPolicy(Policies.ViewAuditLogs, policy =>
        policy.AddRequirements(new RoleRequirement(
            RoleName.GlobalAdmin, RoleName.Auditor)));

    options.AddPolicy(Policies.ManageUsers, policy =>
        policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin)));

    options.AddPolicy(Policies.ConfigureTenant, policy =>
        policy.AddRequirements(new RoleRequirement(
            RoleName.GlobalAdmin, RoleName.SecurityManager)));

    options.AddPolicy(Policies.GenerateAiReports, policy =>
        policy.AddRequirements(new RoleRequirement(
            RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.SecurityAnalyst)));

    options.AddPolicy(Policies.AddComments, policy =>
        policy.AddRequirements(new RoleRequirement(
            RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.SecurityAnalyst, RoleName.AssetOwner)));

    options.AddPolicy(Policies.ManageTeams, policy =>
        policy.AddRequirements(new RoleRequirement(
            RoleName.GlobalAdmin, RoleName.SecurityManager)));
});

builder.Services.AddScoped<IAuthorizationHandler, RoleRequirementHandler>();

// Database
builder.Services.AddDbContext<VigilDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Vigil")));

// Tenant context (scoped - one per request)
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddHttpContextAccessor();

// OpenAPI
builder.Services.AddOpenApi();
builder.Services.AddControllers();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
