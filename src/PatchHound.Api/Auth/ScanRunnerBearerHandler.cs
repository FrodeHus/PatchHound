using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Auth;

public class ScanRunnerBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceProvider serviceProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ScanRunnerBearer";
    public const string RunnerIdClaim = "runner_id";
    public const string TenantIdClaim = "tenant_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.Fail("Empty bearer token");
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();

        var runner = await db.ScanRunners
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.SecretHash == hash);

        if (runner is null)
        {
            return AuthenticateResult.Fail("Invalid bearer token");
        }

        if (!runner.Enabled)
        {
            return AuthenticateResult.Fail("Runner is disabled");
        }

        var claims = new[]
        {
            new Claim(RunnerIdClaim, runner.Id.ToString()),
            new Claim(TenantIdClaim, runner.TenantId.ToString()),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}
