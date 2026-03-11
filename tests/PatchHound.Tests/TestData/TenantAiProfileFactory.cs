using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Tests.TestData;

internal static class TenantAiProfileFactory
{
    public static TenantAiProfile Create(
        Guid tenantId,
        TenantAiProviderType providerType = TenantAiProviderType.OpenAi,
        string name = "Default AI",
        bool isDefault = true,
        bool isEnabled = true,
        string model = "gpt-4.1-mini",
        string systemPrompt = "Prompt",
        decimal temperature = 0.2m,
        decimal? topP = null,
        int maxOutputTokens = 1200,
        int timeoutSeconds = 60,
        string baseUrl = "",
        string deploymentName = "",
        string apiVersion = "",
        string keepAlive = "",
        string secretRef = ""
    ) =>
        TenantAiProfile.Create(
            tenantId,
            name,
            providerType,
            isDefault,
            isEnabled,
            model,
            systemPrompt,
            temperature,
            topP,
            maxOutputTokens,
            timeoutSeconds,
            baseUrl,
            deploymentName,
            apiVersion,
            keepAlive,
            secretRef
        );
}
