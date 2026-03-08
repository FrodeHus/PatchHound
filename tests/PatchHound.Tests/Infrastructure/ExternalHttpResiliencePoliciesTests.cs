using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PatchHound.Infrastructure.ExternalHttp;

namespace PatchHound.Tests.Infrastructure;

public class ExternalHttpResiliencePoliciesTests
{
    [Fact]
    public async Task CreateRetryPolicy_RetriesTooManyRequestsAndEventuallySucceeds()
    {
        var policy = ExternalHttpResiliencePolicies.CreateRetryPolicy(NullLogger.Instance);
        var attempts = 0;

        var response = await policy.ExecuteAsync(() =>
        {
            attempts++;
            if (attempts == 1)
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                    {
                        Headers =
                        {
                            RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                                TimeSpan.Zero
                            ),
                        },
                    }
                );
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        attempts.Should().Be(2);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void GetRetryDelay_UsesRetryAfterHeaderWhenPresent()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.FromSeconds(7)
        );

        var delay = ExternalHttpResiliencePolicies.GetRetryDelay(
            new Polly.DelegateResult<HttpResponseMessage>(response),
            attempt: 1
        );

        delay.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void GetRetryDelay_FallsBackToExponentialBackoff()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        var delay = ExternalHttpResiliencePolicies.GetRetryDelay(
            new Polly.DelegateResult<HttpResponseMessage>(response),
            attempt: 2
        );

        delay.Should().Be(TimeSpan.FromSeconds(4));
    }
}
