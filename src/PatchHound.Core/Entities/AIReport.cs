namespace PatchHound.Core.Entities;

public class AIReport
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RemediationCaseId { get; private set; }
    public Guid? VulnerabilityId { get; private set; }
    public Guid TenantAiProfileId { get; private set; }
    public string Content { get; private set; } = null!;
    public string ProviderType { get; private set; } = null!;
    public string ProfileName { get; private set; } = null!;
    public string Model { get; private set; } = string.Empty;
    public string SystemPromptHash { get; private set; } = string.Empty;
    public decimal Temperature { get; private set; }
    public int MaxOutputTokens { get; private set; }
    public DateTimeOffset GeneratedAt { get; private set; }
    public Guid GeneratedBy { get; private set; }

    public RemediationCase RemediationCase { get; private set; } = null!;
    public TenantAiProfile TenantAiProfile { get; private set; } = null!;
    public Vulnerability? Vulnerability { get; private set; }

    private AIReport() { }

    public static AIReport Create(
        Guid tenantId,
        Guid remediationCaseId,
        Guid tenantAiProfileId,
        string content,
        string providerType,
        string profileName,
        string model,
        string systemPromptHash,
        decimal temperature,
        int maxOutputTokens,
        Guid generatedBy,
        Guid? vulnerabilityId = null)
    {
        return new AIReport
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RemediationCaseId = remediationCaseId,
            VulnerabilityId = vulnerabilityId,
            TenantAiProfileId = tenantAiProfileId,
            Content = content,
            ProviderType = providerType,
            ProfileName = profileName,
            Model = model,
            SystemPromptHash = systemPromptHash,
            Temperature = temperature,
            MaxOutputTokens = maxOutputTokens,
            GeneratedAt = DateTimeOffset.UtcNow,
            GeneratedBy = generatedBy,
        };
    }
}
