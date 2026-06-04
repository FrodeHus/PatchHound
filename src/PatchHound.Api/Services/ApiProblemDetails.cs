using Microsoft.AspNetCore.Mvc;

namespace PatchHound.Api.Services;

public static class ApiProblemDetails
{
    public const string NoActiveTenantTitle = "No active tenant is selected.";
    public const string ForbiddenTenantTitle = "The active user cannot access the requested tenant.";

    public static ProblemDetails NoActiveTenant() => new()
    {
        Title = NoActiveTenantTitle,
        Status = StatusCodes.Status400BadRequest,
    };

    public static ProblemDetails ForbiddenTenant() => new()
    {
        Title = ForbiddenTenantTitle,
        Status = StatusCodes.Status403Forbidden,
    };
}
