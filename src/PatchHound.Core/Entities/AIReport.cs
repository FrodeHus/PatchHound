namespace PatchHound.Core.Entities;

public class AIReport
{
    public Guid Id { get; private set; }
    public Guid VulnerabilityId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Content { get; private set; } = null!;
    public string Provider { get; private set; } = null!;
    public DateTimeOffset GeneratedAt { get; private set; }
    public Guid GeneratedBy { get; private set; }

    private AIReport() { }

    public static AIReport Create(
        Guid vulnerabilityId,
        Guid tenantId,
        string content,
        string provider,
        Guid generatedBy
    )
    {
        return new AIReport
        {
            Id = Guid.NewGuid(),
            VulnerabilityId = vulnerabilityId,
            TenantId = tenantId,
            Content = content,
            Provider = provider,
            GeneratedAt = DateTimeOffset.UtcNow,
            GeneratedBy = generatedBy,
        };
    }
}
