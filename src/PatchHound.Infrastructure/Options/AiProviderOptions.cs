namespace PatchHound.Infrastructure.Options;

public class AiProviderOptions
{
    public string Provider { get; set; } = "AzureOpenAI";
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
    public string? ModelId { get; set; }
}
