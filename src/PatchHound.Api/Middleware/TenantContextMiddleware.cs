using PatchHound.Api.Auth;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Middleware;

public class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var tenantContext = context.RequestServices.GetService<ITenantContext>();
            if (tenantContext is TenantContext tc)
            {
                var dbContext = context.RequestServices.GetRequiredService<PatchHoundDbContext>();
                var teamMembershipRuleService = context.RequestServices.GetRequiredService<PatchHound.Infrastructure.Services.TeamMembershipRuleService>();
                await tc.InitializeAsync(context, dbContext, teamMembershipRuleService);
            }
        }

        await _next(context);
    }
}
