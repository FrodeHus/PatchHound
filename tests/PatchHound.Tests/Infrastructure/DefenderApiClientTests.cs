using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using PatchHound.Infrastructure.VulnerabilitySources;

namespace PatchHound.Tests.Infrastructure;

public class DefenderApiClientTests
{
    private static readonly DefenderClientConfiguration Configuration = new(
        "tenant-id",
        "client-id",
        "client-secret",
        "https://api.securitycenter.microsoft.com",
        "https://api.securitycenter.microsoft.com/.default"
    );

    [Fact]
    public async Task GetVulnerabilityAsync_ReturnsSingleVulnerabilityDetail()
    {
        var handler = new SequenceHttpMessageHandler(
            CreateJsonResponse(
                """
                {
                  "id": "CVE-2026-0001",
                  "name": "Contoso vulnerability",
                  "description": "Catalog description",
                  "severity": "High",
                  "cvssV3": 8.1,
                  "cvssVector": "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H",
                  "publicExploit": true,
                  "exploitVerified": true,
                  "exploitInKit": false,
                  "activeAlert": true,
                  "associatedThreats": ["Malware"],
                  "exploitUris": ["https://example.test/exploit"],
                  "epss": 0.91
                }
                """
            )
        );
        var client = new TestDefenderApiClient(new HttpClient(handler));

        var response = await client.GetVulnerabilityAsync(
            Configuration,
            "CVE-2026-0001",
            CancellationToken.None
        );

        response.Should().NotBeNull();
        response!.Id.Should().Be("CVE-2026-0001");
        response.Name.Should().Be("Contoso vulnerability");
        response.Description.Should().Be("Catalog description");
        response.PublicExploit.Should().BeTrue();
        response.ExploitVerified.Should().BeTrue();
        response.ActiveAlert.Should().BeTrue();
        response.ExploitUris.Should().ContainSingle("https://example.test/exploit");
        response.Epss.Should().Be(0.91m);
        handler
            .RequestUris.Should()
            .ContainSingle(
                "https://api.securitycenter.microsoft.com/api/vulnerabilities/CVE-2026-0001"
            );
        handler.AuthorizationHeaders.Should().OnlyContain(value => value == "Bearer test-token");
    }

    [Fact]
    public async Task GetVulnerabilityAsync_ReturnsNullOnNotFound()
    {
        var handler = new SequenceHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.NotFound)
        );
        var client = new TestDefenderApiClient(new HttpClient(handler));

        var response = await client.GetVulnerabilityAsync(
            Configuration,
            "CVE-2026-4040",
            CancellationToken.None
        );

        response.Should().BeNull();
        handler
            .RequestUris.Should()
            .ContainSingle(
                "https://api.securitycenter.microsoft.com/api/vulnerabilities/CVE-2026-4040"
            );
    }

    [Fact]
    public async Task GetMachineVulnerabilitiesAsync_FollowsAllPagedResponses()
    {
        var handler = new SequenceHttpMessageHandler(
            CreateJsonResponse(
                """
                {
                  "value": [
                    {
                      "id": "entry-1",
                      "cveId": "CVE-2026-0001",
                      "machineId": "machine-1",
                      "machineName": "server-1",
                      "productName": "Contoso App",
                      "productVendor": "Contoso",
                      "productVersion": "1.0",
                      "severity": "High",
                      "cvssV3": 8.1
                    }
                  ],
                  "@odata.nextLink": "https://api.securitycenter.microsoft.com/api/vulnerabilities/machinesVulnerabilities?$skip=1"
                }
                """
            ),
            CreateJsonResponse(
                """
                {
                  "value": [
                    {
                      "id": "entry-2",
                      "cveId": "CVE-2026-0002",
                      "machineId": "machine-2",
                      "machineName": "server-2",
                      "productName": "Contoso App",
                      "productVendor": "Contoso",
                      "productVersion": "1.1",
                      "severity": "Medium",
                      "cvssV3": 5.4
                    }
                  ]
                }
                """
            )
        );
        var client = new TestDefenderApiClient(new HttpClient(handler));

        var response = await client.GetMachineVulnerabilitiesAsync(
            Configuration,
            CancellationToken.None
        );

        response.Value.Should().HaveCount(2);
        response.Value.Select(entry => entry.Id).Should().ContainInOrder("entry-1", "entry-2");
        handler
            .RequestUris.Should()
            .ContainInOrder(
                "https://api.securitycenter.microsoft.com/api/vulnerabilities/machinesVulnerabilities",
                "https://api.securitycenter.microsoft.com/api/vulnerabilities/machinesVulnerabilities?$skip=1"
            );
    }

    [Fact]
    public async Task GetMachinesAsync_FollowsAllPagedResponses()
    {
        var handler = new SequenceHttpMessageHandler(
            CreateJsonResponse(
                """
                {
                  "value": [
                    {
                      "id": "machine-1",
                      "computerDnsName": "server-1.contoso.local"
                    }
                  ],
                  "@odata.nextLink": "https://api.securitycenter.microsoft.com/api/machines?$skip=1"
                }
                """
            ),
            CreateJsonResponse(
                """
                {
                  "value": [
                    {
                      "id": "machine-2",
                      "computerDnsName": "server-2.contoso.local"
                    }
                  ]
                }
                """
            )
        );
        var client = new TestDefenderApiClient(new HttpClient(handler));

        var response = await client.GetMachinesAsync(Configuration, CancellationToken.None);

        response.Value.Should().HaveCount(2);
        response.Value.Select(entry => entry.Id).Should().ContainInOrder("machine-1", "machine-2");
        handler
            .RequestUris.Should()
            .ContainInOrder(
                "https://api.securitycenter.microsoft.com/api/machines",
                "https://api.securitycenter.microsoft.com/api/machines?$skip=1"
            );
    }

    [Fact]
    public async Task GetMachineRecommendationsAsync_FollowsAllPagedResponses()
    {
        var handler = new SequenceHttpMessageHandler(
            CreateJsonResponse(
                """
                {
                  "value": [
                    {
                      "id": "rec-1",
                      "recommendationName": "Update Contoso Agent",
                      "severityScore": 8.2,
                      "publicExploit": true,
                      "activeAlert": false,
                      "associatedThreats": ["Ransomware"],
                      "exposureImpact": 0.8,
                      "configScoreImpact": 0.3
                    }
                  ],
                  "@odata.nextLink": "https://api.securitycenter.microsoft.com/api/machines/machine-1/recommendations?$skip=1"
                }
                """
            ),
            CreateJsonResponse(
                """
                {
                  "value": [
                    {
                      "id": "rec-2",
                      "recommendationName": "Mitigate another issue"
                    }
                  ]
                }
                """
            )
        );
        var client = new TestDefenderApiClient(new HttpClient(handler));

        var response = await client.GetMachineRecommendationsAsync(
            Configuration,
            "machine-1",
            CancellationToken.None
        );

        response.Value.Should().HaveCount(2);
        response.Value[0].PublicExploit.Should().BeTrue();
        response.Value[0].AssociatedThreats.Should().Contain("Ransomware");
        handler
            .RequestUris.Should()
            .ContainInOrder(
                "https://api.securitycenter.microsoft.com/api/machines/machine-1/recommendations",
                "https://api.securitycenter.microsoft.com/api/machines/machine-1/recommendations?$skip=1"
            );
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class TestDefenderApiClient(HttpClient httpClient)
        : DefenderApiClient(httpClient)
    {
        protected override Task<string> GetAccessTokenAsync(
            DefenderClientConfiguration configuration,
            CancellationToken ct
        )
        {
            return Task.FromResult("test-token");
        }
    }

    private sealed class SequenceHttpMessageHandler(params HttpResponseMessage[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<string> RequestUris { get; } = [];
        public List<string?> AuthorizationHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No more responses configured.");
            }

            RequestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
