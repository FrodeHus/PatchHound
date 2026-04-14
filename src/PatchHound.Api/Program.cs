using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.FeatureManagement;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Logging;
using Npgsql;
using PatchHound.Api.Auth;
using PatchHound.Api.Hubs;
using PatchHound.Api.Middleware;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Services.Inventory;

var builder = WebApplication.CreateBuilder(args);
var azureAdConfig = builder.Configuration.GetSection("AzureAd");
var jwtLogPii = builder.Environment.IsDevelopment();

if (jwtLogPii)
{
    IdentityModelEventSource.ShowPII = true;
}

// Authentication - Entra ID multi-tenant
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(azureAdConfig);

// Authentication - Scan runner bearer token (SHA-256 hashed)
builder.Services.AddAuthentication()
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ScanRunnerBearerHandler>(
        ScanRunnerBearerHandler.SchemeName, _ => { });

builder.Services.Configure<JwtBearerOptions>(
    JwtBearerDefaults.AuthenticationScheme,
    options =>
    {
        var configuredAudience = azureAdConfig["Audience"];
        var clientId = azureAdConfig["ClientId"];
        var validAudiences = new[] { configuredAudience, clientId }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var existingEvents = options.Events;

        if (validAudiences.Length > 0)
        {
            options.TokenValidationParameters.ValidAudiences = validAudiences;
        }

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var logger = context
                    .HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("AuthDiagnostics");

                if (existingEvents?.OnTokenValidated is not null)
                {
                    await existingEvents.OnTokenValidated(context);
                }

                var audiences = context
                    .Principal?.Claims.Where(claim => claim.Type == "aud")
                    .Select(claim => claim.Value)
                    .ToArray();

                logger.LogDebug(
                    "JWT token validated. Expected audiences: {ExpectedAudiences}. Token audiences: {TokenAudiences}",
                    validAudiences.Length > 0 ? string.Join(", ", validAudiences) : "<none>",
                    audiences is { Length: > 0 } ? string.Join(", ", audiences) : "<none>"
                );
            },
            OnAuthenticationFailed = async context =>
            {
                var logger = context
                    .HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("AuthDiagnostics");

                if (existingEvents?.OnAuthenticationFailed is not null)
                {
                    await existingEvents.OnAuthenticationFailed(context);
                }

                var authorizationHeader = context.Request.Headers.Authorization.ToString();
                var tokenClaims = TryReadBearerTokenClaims(authorizationHeader);

                logger.LogError(
                    context.Exception,
                    "JWT authentication failed. Expected audiences: {ExpectedAudiences}. Authorization header present: {HasAuthorizationHeader}. Raw header prefix: {AuthorizationPrefix}. Token audience: {TokenAudience}. Token azp: {AuthorizedParty}. Token appid: {AppId}. PII logging enabled: {PiiLoggingEnabled}",
                    validAudiences.Length > 0 ? string.Join(", ", validAudiences) : "<none>",
                    !string.IsNullOrWhiteSpace(authorizationHeader),
                    string.IsNullOrWhiteSpace(authorizationHeader)
                        ? "<missing>"
                        : authorizationHeader.Split(' ')[0][
                            ..Math.Min(authorizationHeader.Split(' ')[0].Length, 10)
                        ],
                    tokenClaims?.Audience ?? "<unavailable>",
                    tokenClaims?.AuthorizedParty ?? "<unavailable>",
                    tokenClaims?.AppId ?? "<unavailable>",
                    jwtLogPii
                );
            },
            OnChallenge = async context =>
            {
                var logger = context
                    .HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("AuthDiagnostics");

                if (existingEvents?.OnChallenge is not null)
                {
                    await existingEvents.OnChallenge(context);
                }

                logger.LogWarning(
                    "JWT challenge triggered. Error: {Error}. Description: {Description}. Expected audiences: {ExpectedAudiences}",
                    context.Error,
                    context.ErrorDescription,
                    validAudiences.Length > 0 ? string.Join(", ", validAudiences) : "<none>"
                );
            },
        };
    }
);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        Policies.ViewVulnerabilities,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
                    RoleName.CustomerViewer,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst,
                    RoleName.AssetOwner,
                    RoleName.Stakeholder,
                    RoleName.Auditor,
                    RoleName.TechnicalManager
                )
            )
    );

    options.AddPolicy(
        Policies.ModifyVulnerabilities,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
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
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
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
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
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
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
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
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
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
                new RoleRequirement(RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.CustomerAdmin)
            )
    );

    options.AddPolicy(
        Policies.ViewAuditLogs,
        policy =>
            policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin, RoleName.Auditor, RoleName.CustomerAdmin))
    );

    options.AddPolicy(
        Policies.ManageUsers,
        policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin, RoleName.CustomerAdmin))
    );

    options.AddPolicy(
        Policies.ViewTeams,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
                    RoleName.CustomerViewer,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst,
                    RoleName.AssetOwner,
                    RoleName.Stakeholder,
                    RoleName.Auditor,
                    RoleName.TechnicalManager
                )
            )
    );

    options.AddPolicy(
        Policies.ViewTenants,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
                    RoleName.CustomerViewer,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst,
                    RoleName.AssetOwner,
                    RoleName.Stakeholder,
                    RoleName.Auditor,
                    RoleName.TechnicalManager
                )
            )
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
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
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
                    RoleName.CustomerAdmin,
                    RoleName.CustomerOperator,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst,
                    RoleName.AssetOwner
                )
            )
    );

    options.AddPolicy(
        Policies.ManageTeams,
        policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin))
    );

    options.AddPolicy(
        Policies.ViewApprovalTasks,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.TechnicalManager, RoleName.CustomerAdmin, RoleName.CustomerOperator)
            )
    );

    options.AddPolicy(
        Policies.ResolveApprovalTask,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(RoleName.GlobalAdmin, RoleName.SecurityManager, RoleName.TechnicalManager, RoleName.CustomerAdmin, RoleName.CustomerOperator)
            )
    );

    options.AddPolicy(
        Policies.ManageVault,
        policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin))
    );

    options.AddPolicy(
        Policies.ManageWorkflows,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(RoleName.GlobalAdmin, RoleName.SecurityManager)
            )
    );

    options.AddPolicy(
        Policies.PerformMaintenance,
        policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin))
    );

    options.AddPolicy(
        Policies.ManageGlobalSettings,
        policy => policy.AddRequirements(new RoleRequirement(RoleName.GlobalAdmin))
    );

    options.AddPolicy(
        Policies.ManageAuthenticatedScans,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.CustomerAdmin,
                    RoleName.SecurityManager
                )
            ));

    options.AddPolicy(
        Policies.CreateDecision,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.SecurityManager,
                    RoleName.TechnicalManager
                )
            ));

    options.AddPolicy(
        Policies.ApproveDecision,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.SecurityManager,
                    RoleName.TechnicalManager
                )
            ));

    options.AddPolicy(
        Policies.AddRecommendation,
        policy =>
            policy.AddRequirements(
                new RoleRequirement(
                    RoleName.GlobalAdmin,
                    RoleName.SecurityManager,
                    RoleName.SecurityAnalyst
                )
            ));
});

builder.Services.AddScoped<IAuthorizationHandler, RoleRequirementHandler>();

// Infrastructure services (database, repositories, email, AI providers, vulnerability sources)
builder.Services.AddPatchHoundInfrastructure(builder.Configuration);

// Tenant context (scoped - one per request)
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ISoftwareProductResolver, SoftwareProductResolver>();
builder.Services.AddScoped<IDeviceResolver, DeviceResolver>();
builder.Services.AddScoped<IStagedDeviceMergeService, StagedDeviceMergeService>();
builder.Services.AddScoped<PatchHound.Api.Services.TenantSoftwareAliasResolver>();
builder.Services.AddScoped<PatchHound.Api.Services.DashboardQueryService>();
builder.Services.AddScoped<PatchHound.Api.Services.VulnerabilityDetailQueryService>();
builder.Services.AddScoped<PatchHound.Api.Services.AssetDetailQueryService>();
builder.Services.AddScoped<PatchHound.Api.Services.DeviceDetailQueryService>();
builder.Services.AddScoped<PatchHound.Api.Services.RemediationDecisionQueryService>();
builder.Services.AddScoped<PatchHound.Api.Services.RemediationWorkflowAuthorizationService>();
builder.Services.AddScoped<PatchHound.Api.Services.BlockedTenantAccessLogger>();
builder.Services.AddScoped<PatchHound.Api.Services.ApprovalTaskQueryService>();
builder.Services.AddScoped<PatchHound.Api.Services.RemediationTaskQueryService>();
builder.Services.AddHttpContextAccessor();

// SignalR
builder.Services.AddSignalR();
builder.Services.AddScoped<IRealTimeNotifier, SignalRNotifier<NotificationHub>>();

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var partitionKey = context.User.Identity?.Name
            ?? context.User.FindFirst("runner_id")?.Value
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
            }
        );
    });
    options.RejectionStatusCode = 429;
});

// CORS
var frontendOrigin = builder.Configuration["Frontend:Origin"];
if (!string.IsNullOrWhiteSpace(frontendOrigin))
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
            policy.WithOrigins(frontendOrigin).AllowAnyHeader().AllowAnyMethod().AllowCredentials()
        );
    });
}

// OpenAPI
builder.Services.AddOpenApi();
builder.Services.AddControllers();

// Feature management (database-backed overrides + config defaults)
// Register the custom provider as IFeatureDefinitionProvider — FeatureManager resolves it via DI.
builder.Services.AddScoped<Microsoft.FeatureManagement.IFeatureDefinitionProvider,
    PatchHound.Infrastructure.FeatureFlags.DatabaseFeatureDefinitionProvider>();
builder.Services.AddFeatureManagement(builder.Configuration.GetSection("FeatureManagement"));

var app = builder.Build();

// Apply pending database migrations on startup
using (var scope = app.Services.CreateScope())
{
    Console.WriteLine("[startup] PatchHound.Api starting database migration check");
    var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
    var migrationLogger = scope
        .ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("StartupMigration");
    await MigrateWithRetryAsync(dbContext, migrationLogger, app.Lifetime.ApplicationStopping);
    Console.WriteLine("[startup] PatchHound.Api database migration check completed");
}

Console.WriteLine("[startup] PatchHound.Api application configured, starting web host");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}

app.UseMiddleware<ExceptionHandlerMiddleware>();
app.UseHttpsRedirection();

// Security headers
app.Use(
    async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; connect-src 'self' wss:; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; object-src 'none'; frame-ancestors 'none';";
        await next();
    }
);

if (!string.IsNullOrWhiteSpace(frontendOrigin))
{
    app.UseCors();
}

app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");
await app.RunAsync();

static async Task MigrateWithRetryAsync(
    PatchHoundDbContext dbContext,
    ILogger logger,
    CancellationToken ct
)
{
    const int maxAttempts = 12;

    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await dbContext.Database.MigrateAsync(ct);
            return;
        }
        catch (Exception ex)
            when (attempt < maxAttempts
                && (
                    ex is NpgsqlException
                    || ex.InnerException is NpgsqlException
                    || ex is TimeoutException
                )
            )
        {
            logger.LogWarning(
                ex,
                "Database migration startup attempt {Attempt}/{MaxAttempts} failed. Retrying in 5 seconds.",
                attempt,
                maxAttempts
            );
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
}

static TokenDiagnostics? TryReadBearerTokenClaims(string authorizationHeader)
{
    if (string.IsNullOrWhiteSpace(authorizationHeader))
    {
        return null;
    }

    const string bearerPrefix = "Bearer ";
    if (!authorizationHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    var token = authorizationHeader[bearerPrefix.Length..].Trim();
    var segments = token.Split('.');
    if (segments.Length < 2)
    {
        return null;
    }

    try
    {
        var payload = segments[1].Replace('-', '+').Replace('_', '/');

        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new TokenDiagnostics(
            GetStringOrArray(root, "aud"),
            GetString(root, "azp"),
            GetString(root, "appid")
        );
    }
    catch
    {
        return null;
    }
}

static string? GetString(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property))
    {
        return null;
    }

    return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
}

static string? GetStringOrArray(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property))
    {
        return null;
    }

    return property.ValueKind switch
    {
        JsonValueKind.String => property.GetString(),
        JsonValueKind.Array => string.Join(
            ", ",
            property.EnumerateArray().Select(item => item.ToString())
        ),
        _ => property.ToString(),
    };
}

sealed record TokenDiagnostics(string? Audience, string? AuthorizedParty, string? AppId);
