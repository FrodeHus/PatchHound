using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace PatchHound.Infrastructure.ExternalHttp;

internal static class ExternalHttpResiliencePolicies
{
    private const int RetryCount = 4;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    public static IHttpClientBuilder AddExternalHttpPolicies(
        this IHttpClientBuilder builder,
        int maxConnectionsPerServer
    )
    {
        return builder
            .ConfigurePrimaryHttpMessageHandler(() =>
                new SocketsHttpHandler
                {
                    AutomaticDecompression =
                        DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    MaxConnectionsPerServer = maxConnectionsPerServer,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                }
            )
            .AddPolicyHandler(
                (serviceProvider, _) =>
                {
                    var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                    return CreateRetryPolicy(
                        loggerFactory.CreateLogger("PatchHound.ExternalHttpPolicy")
                    );
                }
            );
    }

    internal static IAsyncPolicy<HttpResponseMessage> CreateRetryPolicy(ILogger logger)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(ShouldRetry)
            .WaitAndRetryAsync(
                RetryCount,
                (attempt, outcome, _) => GetRetryDelay(outcome, attempt),
                (outcome, delay, attempt, _) =>
                {
                    logger.LogWarning(
                        "Retrying outbound HTTP request after {DelayMs}ms. Attempt {Attempt}/{RetryCount}. StatusCode: {StatusCode}.",
                        (int)delay.TotalMilliseconds,
                        attempt,
                        RetryCount,
                        outcome.Result?.StatusCode
                    );

                    return Task.CompletedTask;
                }
            );
    }

    internal static TimeSpan GetRetryDelay(DelegateResult<HttpResponseMessage> outcome, int attempt)
    {
        var retryAfter = outcome.Result?.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
            {
                return ClampDelay(delta);
            }

            if (retryAfter.Date is { } date)
            {
                var absoluteDelay = date - DateTimeOffset.UtcNow;
                if (absoluteDelay > TimeSpan.Zero)
                {
                    return ClampDelay(absoluteDelay);
                }
            }
        }

        var exponentialDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        return ClampDelay(exponentialDelay);
    }

    private static bool ShouldRetry(HttpResponseMessage response)
    {
        return response.StatusCode == HttpStatusCode.TooManyRequests;
    }

    private static TimeSpan ClampDelay(TimeSpan delay)
    {
        return delay > MaxRetryDelay ? MaxRetryDelay : delay;
    }
}
