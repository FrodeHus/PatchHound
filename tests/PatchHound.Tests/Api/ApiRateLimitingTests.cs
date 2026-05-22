using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using PatchHound.Api.RateLimiting;

namespace PatchHound.Tests.Api;

public class ApiRateLimitingTests
{
    [Fact]
    public void GetPartitionKey_UsesAuthenticatedObjectIdBeforeRemoteAddress()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim("oid", "user-1")],
                    authenticationType: "Bearer"
                )
            ),
        };
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");

        var partitionKey = ApiRateLimitingPolicy.GetPartitionKey(context);

        partitionKey.Should().Be("user:user-1");
    }

    [Fact]
    public void GetPartitionKey_UsesRunnerIdForScanRunnerRequests()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim("runner_id", "runner-1")],
                    authenticationType: "ScanRunner"
                )
            ),
        };

        var partitionKey = ApiRateLimitingPolicy.GetPartitionKey(context);

        partitionKey.Should().Be("runner:runner-1");
    }

    [Fact]
    public void CreateFixedWindowOptions_AllowsFrontendNavigationBursts()
    {
        var options = ApiRateLimitingPolicy.CreateFixedWindowOptions();

        options.PermitLimit.Should().BeGreaterThanOrEqualTo(1_000);
        options.Window.Should().Be(TimeSpan.FromMinutes(1));
        options.QueueLimit.Should().BeGreaterThanOrEqualTo(100);
    }
}
