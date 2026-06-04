using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using PatchHound.Api.Services;
using PatchHound.Core.Interfaces;

namespace PatchHound.Tests.Api;

public class ApiRequestContextTests
{
    [Fact]
    public void RequireCurrentTenant_ReturnsSharedProblemDetailsWhenTenantIsMissing()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns((Guid?)null);
        var requestContext = new ApiRequestContext(tenantContext);

        var action = requestContext.RequireCurrentTenant(out var tenantId);

        tenantId.Should().BeEmpty();
        var badRequest = action.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeOfType<ProblemDetails>()
            .Subject.Title.Should().Be(ApiProblemDetails.NoActiveTenantTitle);
    }

    [Fact]
    public void RequireTenantAccess_ReturnsSharedProblemDetailsWhenTenantIsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.HasAccessToTenant(tenantId).Returns(false);
        var requestContext = new ApiRequestContext(tenantContext);

        var action = requestContext.RequireTenantAccess(tenantId);

        var forbidden = action.Should().BeOfType<ObjectResult>().Subject;
        forbidden.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        forbidden.Value.Should().BeOfType<ProblemDetails>()
            .Subject.Title.Should().Be(ApiProblemDetails.ForbiddenTenantTitle);
    }
}
