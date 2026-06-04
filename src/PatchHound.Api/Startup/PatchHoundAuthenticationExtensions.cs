using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Logging;
using PatchHound.Api.Auth;

namespace PatchHound.Api.Startup;

public static class PatchHoundAuthenticationExtensions
{
    public static IServiceCollection AddPatchHoundAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var azureAdConfig = configuration.GetSection("AzureAd");
        var jwtLogPii = environment.IsDevelopment();

        if (jwtLogPii)
        {
            IdentityModelEventSource.ShowPII = true;
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(azureAdConfig);

        services.AddAuthentication()
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ScanRunnerBearerHandler>(
                ScanRunnerBearerHandler.SchemeName, _ => { });

        services.Configure<JwtBearerOptions>(
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
                        var tokenClaims = JwtBearerTokenDiagnosticsReader.TryRead(authorizationHeader);
                        var authorizationPrefix = string.IsNullOrWhiteSpace(authorizationHeader)
                            ? "<missing>"
                            : authorizationHeader.Split(' ')[0][
                                ..Math.Min(authorizationHeader.Split(' ')[0].Length, 10)
                            ];

                        logger.LogError(
                            context.Exception,
                            "JWT authentication failed. Expected audiences: {ExpectedAudiences}. Authorization header present: {HasAuthorizationHeader}. Raw header prefix: {AuthorizationPrefix}. Token audience: {TokenAudience}. Token azp: {AuthorizedParty}. Token appid: {AppId}. PII logging enabled: {PiiLoggingEnabled}",
                            validAudiences.Length > 0 ? string.Join(", ", validAudiences) : "<none>",
                            !string.IsNullOrWhiteSpace(authorizationHeader),
                            authorizationPrefix,
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

        return services;
    }
}
