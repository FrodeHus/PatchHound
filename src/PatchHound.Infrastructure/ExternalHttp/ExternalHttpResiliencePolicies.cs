using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace PatchHound.Infrastructure.ExternalHttp;

internal static class ExternalHttpResiliencePolicies
{
    private const int DefaultRetryCount = 4;
    private const int DefenderRetryCount = 6;
    private static readonly TimeSpan DefaultMaxRetryDelay = TimeSpan.FromSeconds(30);

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

    public static IHttpClientBuilder AddDefenderHttpPolicies(this IHttpClientBuilder builder)
    {
        return builder
            .ConfigurePrimaryHttpMessageHandler(() =>
                new SocketsHttpHandler
                {
                    AutomaticDecompression =
                        DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    // Defender explicitly documents throttling and asks clients to reduce request volume.
                    // Keep parallelism tight and honor Retry-After for backoff.
                    MaxConnectionsPerServer = 2,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                }
            )
            .AddPolicyHandler(
                (serviceProvider, _) =>
                {
                    var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                    return CreateDefenderRetryPolicy(
                        loggerFactory.CreateLogger("PatchHound.DefenderHttpPolicy")
                    );
                }
            );
    }

    internal static IAsyncPolicy<HttpResponseMessage> CreateRetryPolicy(ILogger logger)
    {
        return CreateRetryPolicy(
            logger,
            DefaultRetryCount,
            DefaultMaxRetryDelay,
            honorRetryAfterFully: false
        );
    }

    internal static IAsyncPolicy<HttpResponseMessage> CreateDefenderRetryPolicy(ILogger logger)
    {
        return CreateRetryPolicy(
            logger,
            DefenderRetryCount,
            maxRetryDelay: TimeSpan.FromMinutes(5),
            honorRetryAfterFully: true
        );
    }

    private static IAsyncPolicy<HttpResponseMessage> CreateRetryPolicy(
        ILogger logger,
        int retryCount,
        TimeSpan maxRetryDelay,
        bool honorRetryAfterFully
    )
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(ShouldRetry)
            .WaitAndRetryAsync(
                retryCount,
                (attempt, outcome, _) =>
                    GetRetryDelay(outcome, attempt, maxRetryDelay, honorRetryAfterFully),
                (outcome, delay, attempt, _) =>
                {
                    logger.LogWarning(
                        "Retrying outbound HTTP request after {DelayMs}ms. Attempt {Attempt}/{RetryCount}. StatusCode: {StatusCode}.",
                        (int)delay.TotalMilliseconds,
                        attempt,
                        retryCount,
                        outcome.Result?.StatusCode
                    );

                    return Task.CompletedTask;
                }
            );
    }

    internal static TimeSpan GetRetryDelay(
        DelegateResult<HttpResponseMessage> outcome,
        int attempt,
        TimeSpan maxRetryDelay,
        bool honorRetryAfterFully
    )
    {
        var retryAfter = outcome.Result?.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
            {
                return honorRetryAfterFully ? delta : ClampDelay(delta, maxRetryDelay);
            }

            if (retryAfter.Date is { } date)
            {
                var absoluteDelay = date - DateTimeOffset.UtcNow;
                if (absoluteDelay > TimeSpan.Zero)
                {
                    return honorRetryAfterFully
                        ? absoluteDelay
                        : ClampDelay(absoluteDelay, maxRetryDelay);
                }
            }
        }

        var exponentialDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        return ClampDelay(exponentialDelay, maxRetryDelay);
    }

    private static bool ShouldRetry(HttpResponseMessage response)
    {
        return response.StatusCode == HttpStatusCode.TooManyRequests;
    }

    private static TimeSpan ClampDelay(TimeSpan delay, TimeSpan maxRetryDelay)
    {
        return delay > maxRetryDelay ? maxRetryDelay : delay;
    }
}
