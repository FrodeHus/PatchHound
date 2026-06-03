namespace PatchHound.Core.Enums;

public static class TenantAiProviderTypeExtensions
{
    /// <summary>
    /// True when the provider sends data off-box (cloud). Unknown values are treated as
    /// external so a new provider defaults to the safe (redacted) posture until classified.
    /// </summary>
    public static bool IsExternal(this TenantAiProviderType providerType) =>
        providerType switch
        {
            TenantAiProviderType.Ollama => false,
            TenantAiProviderType.AzureOpenAi => true,
            TenantAiProviderType.OpenAi => true,
            _ => true,
        };
}
