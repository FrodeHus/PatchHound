using FluentAssertions;
using PatchHound.Core.Enums;
using Xunit;

namespace PatchHound.Tests.Core.OperationalContext;

public class TenantAiProviderTypeExtensionsTests
{
    [Theory]
    [InlineData(TenantAiProviderType.OpenAi, true)]
    [InlineData(TenantAiProviderType.AzureOpenAi, true)]
    [InlineData(TenantAiProviderType.Ollama, false)]
    public void IsExternal_classifies_cloud_providers_as_external(
        TenantAiProviderType type, bool expected)
    {
        type.IsExternal().Should().Be(expected);
    }

    [Fact]
    public void IsExternal_treats_unknown_values_as_external()
    {
        ((TenantAiProviderType)999).IsExternal().Should().BeTrue();
    }
}
