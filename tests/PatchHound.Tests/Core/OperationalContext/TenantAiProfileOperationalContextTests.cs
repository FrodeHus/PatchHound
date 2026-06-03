using FluentAssertions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using Xunit;

namespace PatchHound.Tests.Core.OperationalContext;

public class TenantAiProfileOperationalContextTests
{
    private static TenantAiProfile NewProfile() => TenantAiProfile.Create(
        tenantId: Guid.NewGuid(), name: "p", providerType: TenantAiProviderType.Ollama,
        isDefault: true, isEnabled: true, model: "m", systemPrompt: "s",
        temperature: 0.1m, topP: null, maxOutputTokens: 1000, timeoutSeconds: 30);

    [Fact]
    public void Create_defaults_operational_context_to_disabled_safe_values()
    {
        var profile = NewProfile();

        profile.AllowOperationalContext.Should().BeFalse();
        profile.OperationalContextMode.Should().Be(OperationalContextMode.StructuredOnly);
        profile.MaxOperationalContextTokens.Should().Be(3000);
        profile.IncludeDeviceNamesInContext.Should().BeTrue();
        profile.IncludeUserNamesInContext.Should().BeFalse();
    }

    [Fact]
    public void Update_applies_operational_context_settings()
    {
        var profile = NewProfile();

        profile.Update(
            name: "p", isDefault: true, isEnabled: true, model: "m", systemPrompt: "s",
            temperature: 0.1m, topP: null, maxOutputTokens: 1000, timeoutSeconds: 30,
            baseUrl: "", deploymentName: "", apiVersion: "", keepAlive: "", secretRef: "",
            allowExternalResearch: false, webResearchMode: TenantAiWebResearchMode.Disabled,
            includeCitations: true, maxResearchSources: 5, allowedDomains: "", numCtx: null,
            responseFormat: TenantAiResponseFormat.None, researchSourceKey: "",
            allowOperationalContext: true,
            operationalContextMode: OperationalContextMode.StructuredOnly,
            maxOperationalContextTokens: 1500,
            includeDeviceNamesInContext: false,
            includeUserNamesInContext: false);

        profile.AllowOperationalContext.Should().BeTrue();
        profile.MaxOperationalContextTokens.Should().Be(1500);
        profile.IncludeDeviceNamesInContext.Should().BeFalse();
    }
}
