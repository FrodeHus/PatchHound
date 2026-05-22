using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace PatchHound.Api.RateLimiting;

public static class ApiRateLimitingPolicy
{
    private static readonly string[] UserClaimTypes =
    [
        "oid",
        "sub",
        "preferred_username",
        "upn",
        "name",
    ];

    public static string GetPartitionKey(HttpContext context)
    {
        var runnerId = GetClaimValue(context, "runner_id");
        if (!string.IsNullOrWhiteSpace(runnerId))
        {
            return $"runner:{runnerId}";
        }

        foreach (var claimType in UserClaimTypes)
        {
            var value = GetClaimValue(context, claimType);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return $"user:{value}";
            }
        }

        if (!string.IsNullOrWhiteSpace(context.User.Identity?.Name))
        {
            return $"user:{context.User.Identity.Name}";
        }

        var remoteAddress = context.Connection.RemoteIpAddress?.ToString();
        return !string.IsNullOrWhiteSpace(remoteAddress)
            ? $"ip:{remoteAddress}"
            : "anonymous";
    }

    public static FixedWindowRateLimiterOptions CreateFixedWindowOptions()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1_200,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 200,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        };
    }

    private static string? GetClaimValue(HttpContext context, string claimType)
    {
        return context.User.FindFirst(claimType)?.Value;
    }
}
