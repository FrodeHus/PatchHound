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
}
