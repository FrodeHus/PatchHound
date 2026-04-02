using FluentAssertions;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Tests.Infrastructure;

public class AdvancedToolTemplateRendererTests
{
    [Fact]
    public void Render_QuotesStringPlaceholdersAsKqlStringLiterals()
    {
        var query = "DeviceInfo | where DeviceName == {{deviceName}}";

        var rendered = AdvancedToolTemplateRenderer.Render(
            query,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["deviceName"] = "fenris",
            }
        );

        rendered.Should().Be("DeviceInfo | where DeviceName == 'fenris'");
    }

    [Fact]
    public void Render_EscapesSingleQuotesInsideStringValues()
    {
        var query = "DeviceInfo | where DeviceName == {{deviceName}}";

        var rendered = AdvancedToolTemplateRenderer.Render(
            query,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["deviceName"] = "O'Brien",
            }
        );

        rendered.Should().Be("DeviceInfo | where DeviceName == 'O''Brien'");
    }
}
