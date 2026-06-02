using FluentAssertions;
using PatchHound.Core.Models.OperationalContext;
using PatchHound.Core.Services.OperationalContext;
using Xunit;

namespace PatchHound.Tests.Core.OperationalContext;

public class OperationalContextRedactorTests
{
    [Fact]
    public void Redacts_device_labels_and_facts_for_external_provider()
    {
        var citations = new List<OperationalContextCitation>
        {
            new() { Key = "device-risk-top-1", EntityType = "Device", EntityId = Guid.NewGuid(),
                    Label = "host-123", Fact = "Device host-123 risk score 937" },
        };
        var options = new AiOperationalContextOptions
        {
            ProviderIsExternal = true, IncludeDeviceNames = false,
        };

        var redacted = new OperationalContextRedactor().RedactCitations(citations, options);

        redacted[0].Label.Should().Be("device-1");
        redacted[0].Fact.Should().NotContain("host-123");
        redacted[0].Fact.Should().Contain("device-1");
    }

    [Fact]
    public void Keeps_device_names_when_not_redacting()
    {
        var citations = new List<OperationalContextCitation>
        {
            new() { Key = "k", EntityType = "Device", EntityId = Guid.NewGuid(),
                    Label = "host-123", Fact = "Device host-123 risk score 937" },
        };
        var options = new AiOperationalContextOptions
        {
            ProviderIsExternal = false, IncludeDeviceNames = true,
        };

        var redacted = new OperationalContextRedactor().RedactCitations(citations, options);

        redacted[0].Label.Should().Be("host-123");
    }

    [Fact]
    public void Word_boundary_replacement_does_not_corrupt_tokens_that_contain_label_as_substring()
    {
        // Label "web" must not partially replace "webserver-prod"
        var citations = new List<OperationalContextCitation>
        {
            new() { Key = "device-risk-top-1", EntityType = "Device", EntityId = Guid.NewGuid(),
                    Label = "web", Fact = "web is at webserver-prod" },
        };
        var options = new AiOperationalContextOptions
        {
            ProviderIsExternal = true, IncludeDeviceNames = false,
        };

        var redacted = new OperationalContextRedactor().RedactCitations(citations, options);

        redacted[0].Label.Should().Be("device-1");
        redacted[0].Fact.Should().StartWith("device-1 is at ");
        redacted[0].Fact.Should().Contain("webserver-prod");
        redacted[0].Fact.Should().NotContain(" web ");
    }

    [Fact]
    public void Non_device_citation_passes_through_untouched_even_when_redacting()
    {
        var citations = new List<OperationalContextCitation>
        {
            new() { Key = "vuln-1", EntityType = "Vulnerability", EntityId = Guid.NewGuid(),
                    Label = "CVE-2024-1234", Fact = "Critical vulnerability in libssl" },
        };
        var options = new AiOperationalContextOptions
        {
            ProviderIsExternal = true, IncludeDeviceNames = false,
        };

        var redacted = new OperationalContextRedactor().RedactCitations(citations, options);

        redacted.Should().HaveCount(1);
        redacted[0].Label.Should().Be("CVE-2024-1234");
        redacted[0].Fact.Should().Be("Critical vulnerability in libssl");
    }

    [Fact]
    public void External_provider_with_IncludeDeviceNames_true_keeps_real_device_label()
    {
        var citations = new List<OperationalContextCitation>
        {
            new() { Key = "device-risk-top-1", EntityType = "Device", EntityId = Guid.NewGuid(),
                    Label = "prod-server-42", Fact = "Device prod-server-42 risk score 750" },
        };
        var options = new AiOperationalContextOptions
        {
            ProviderIsExternal = true, IncludeDeviceNames = true,
        };

        var redacted = new OperationalContextRedactor().RedactCitations(citations, options);

        redacted[0].Label.Should().Be("prod-server-42");
        redacted[0].Fact.Should().Contain("prod-server-42");
    }
}
