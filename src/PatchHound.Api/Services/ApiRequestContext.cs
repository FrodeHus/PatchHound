using Microsoft.AspNetCore.Mvc;
using PatchHound.Core.Interfaces;

namespace PatchHound.Api.Services;

public sealed class ApiRequestContext
{
    private readonly ITenantContext _tenantContext;

    public ApiRequestContext(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public ActionResult? RequireCurrentTenant(out Guid tenantId)
    {
        if (_tenantContext.CurrentTenantId is Guid currentTenantId)
        {
            tenantId = currentTenantId;
            return null;
        }

        tenantId = Guid.Empty;
        return new BadRequestObjectResult(ApiProblemDetails.NoActiveTenant());
    }

    public ActionResult? RequireTenantAccess(Guid tenantId)
    {
        if (_tenantContext.HasAccessToTenant(tenantId))
            return null;

        return new ObjectResult(ApiProblemDetails.ForbiddenTenant())
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}
