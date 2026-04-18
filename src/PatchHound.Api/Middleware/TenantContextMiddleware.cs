using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Services;
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

                var requestedTenantIdHeader = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
                if (Guid.TryParse(requestedTenantIdHeader, out var requestedTenantId))
                {
                    var isPendingDeletion = await dbContext.Tenants
                        .IgnoreQueryFilters()
                        .Where(t => t.Id == requestedTenantId && t.IsPendingDeletion)
                        .AnyAsync(context.RequestAborted);

                    if (isPendingDeletion)
                    {
                        context.Response.StatusCode = 410;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(
                            """{"errorCode":"tenant_pending_deletion"}""",
                            context.RequestAborted
                        );
                        return;
                    }
                }
            }
        }

        await _next(context);

        if (context.Items.TryGetValue(TenantContext.BlockedTenantAccessItemsKey, out var existing)
            && existing is List<BlockedTenantAccessAttempt> attempts
            && attempts.Count > 0
            && context.User.Identity?.IsAuthenticated == true)
        {
            var logger = context.RequestServices.GetService<BlockedTenantAccessLogger>();
            var dbContext = context.RequestServices.GetService<PatchHoundDbContext>();
            if (logger is not null && dbContext is not null)
            {
                await logger.LogAsync(attempts, context.RequestAborted);
                await dbContext.SaveChangesAsync(context.RequestAborted);
            }
        }
    }
}
