using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class AnalystRecommendation
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SoftwareAssetId { get; private set; }
    public Guid? TenantVulnerabilityId { get; private set; }
    public RemediationOutcome RecommendedOutcome { get; private set; }
    public string Rationale { get; private set; } = null!;
    public string? PriorityOverride { get; private set; }
    public Guid AnalystId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AnalystRecommendation() { }

    public static AnalystRecommendation Create(
        Guid tenantId,
        Guid softwareAssetId,
        RemediationOutcome recommendedOutcome,
        string rationale,
        Guid analystId,
        Guid? tenantVulnerabilityId = null,
        string? priorityOverride = null
    )
    {
        if (string.IsNullOrWhiteSpace(rationale))
            throw new ArgumentException("Rationale is required.");

        return new AnalystRecommendation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SoftwareAssetId = softwareAssetId,
            TenantVulnerabilityId = tenantVulnerabilityId,
            RecommendedOutcome = recommendedOutcome,
            Rationale = rationale,
            PriorityOverride = priorityOverride,
            AnalystId = analystId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
