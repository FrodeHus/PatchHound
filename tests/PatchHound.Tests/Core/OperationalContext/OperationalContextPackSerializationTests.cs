using System.Text.Json;
using FluentAssertions;
using PatchHound.Core.Models.OperationalContext;
using Xunit;

namespace PatchHound.Tests.Core.OperationalContext;

public class OperationalContextPackSerializationTests
{
    [Fact]
    public void Pack_serializes_with_camelCase_stable_field_names()
    {
        var pack = new OperationalContextPack
        {
            ContextKind = "RemediationCase",
            Subject = new() { RemediationCaseId = Guid.Empty },
            Scope = new() { OpenExposureCount = 42, AffectedDeviceCount = 30 },
            Limits = new() { TopDeviceLimit = 10, ExposureLimit = 100, Truncated = false },
        };

        var json = JsonSerializer.Serialize(pack, OperationalContextPack.SerializerOptions);

        json.Should().Contain("\"contextKind\":\"RemediationCase\"");
        json.Should().Contain("\"openExposureCount\":42");
        json.Should().Contain("\"truncated\":false");
    }
}
