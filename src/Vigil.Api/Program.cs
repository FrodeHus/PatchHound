using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Vigil.Api.Auth;
using Vigil.Api.Middleware;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;
using Vigil.Core.Services;
using Vigil.Infrastructure.Data;
using Vigil.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Authentication - Entra ID multi-tenant
builder
    .Services.AddAuthentication()
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        Policies.ViewVulnerabilities,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst,
                    RoleName.AssetOwner,
                    RoleName.Stakeholder,
                    RoleName.Auditor
                )
            )
    );

    options.AddPolicy(
        Policies.ModifyVulnerabilities,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst
                )
            )
    );

    options.AddPolicy(
        Policies.AdjustSeverity,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst
                )
            )
    );

    options.AddPolicy(
        Policies.AssignTasks,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst
                )
            )
    );

    options.AddPolicy(
        Policies.UpdateTaskStatus,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst,
                    RoleName.AssetOwner
                )
            )
    );

    options.AddPolicy(
        Policies.RequestRiskAcceptance,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst,
                    RoleName.AssetOwner
                )
            )
    );

    options.AddPolicy(
        Policies.ApproveRiskAcceptance,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(RoleName.GlobalAdmin, RoleName.SecurityManager)
            )
    );

    options.AddPolicy(
        Policies.ManageCampaigns,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst
                )
            )
    );

    options.AddPolicy(
        Policies.ViewAuditLogs,
        policy =>
            policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin, RoleName.Auditor))
    );

    options.AddPolicy(
        Policies.ManageUsers,
        policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin))
    );

    options.AddPolicy(
        Policies.ConfigureTenant,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(RoleName.GlobalAdmin, RoleName.SecurityManager)
            )
    );

    options.AddPolicy(
        Policies.GenerateAiReports,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst
                )
            )
    );

    options.AddPolicy(
        Policies.AddComments,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst,
                    RoleName.AssetOwner
                )
            )
    );

    options.AddPolicy(
        Policies.ManageTeams,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(RoleName.GlobalAdmin, RoleName.SecurityManager)
            )
    );
});

builder.Services.AddScoped<IAuthorizationHandler, RoleRequirementHandler>();

// Database
builder.Services.AddDbContext<VigilDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Vigil"))
);

// Tenant context (scoped - one per request)
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddHttpContextAccessor();

// Application services
builder.Services.AddScoped<VulnerabilityService>();
builder.Services.AddScoped<RemediationTaskService>();
builder.Services.AddScoped<AssetService>();
builder.Services.AddScoped<CampaignService>();
builder.Services.AddScoped<RiskAcceptanceService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<TeamService>();

// Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ITeamRepository, TeamRepository>();

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy
            .WithOrigins(builder.Configuration["Frontend:Origin"] ?? "http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
    );
});

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.Identity?.Name
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
            }
        )
    );
    options.RejectionStatusCode = 429;
});

// OpenAPI
builder.Services.AddOpenApi();
builder.Services.AddControllers();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<ExceptionHandlerMiddleware>();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseCors();
app.UseRateLimiter();
app.MapControllers();
app.Run();
