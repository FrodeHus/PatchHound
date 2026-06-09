namespace PatchHound.Core.Entities;

public class RecommendationContextSnapshot
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RemediationCaseId { get; private set; }
    public string ContextJson { get; private set; } = null!;
    public string ContextHash { get; private set; } = null!;
    public string CitationsJson { get; private set; } = "[]";
    public Guid GeneratedBy { get; private set; }
    public DateTimeOffset GeneratedAt { get; private set; }

    private RecommendationContextSnapshot() { }

    public static RecommendationContextSnapshot Create(
        Guid tenantId,
        Guid remediationCaseId,
        string contextJson,
        string contextHash,
        string citationsJson,
        Guid generatedBy)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (remediationCaseId == Guid.Empty)
            throw new ArgumentException("RemediationCaseId is required.", nameof(remediationCaseId));
        if (generatedBy == Guid.Empty)
            throw new ArgumentException("GeneratedBy is required.", nameof(generatedBy));
        if (string.IsNullOrWhiteSpace(contextJson))
            throw new ArgumentException("ContextJson is required.", nameof(contextJson));
        if (contextHash is { Length: > 64 })
            throw new ArgumentException("ContextHash must be at most 64 characters.", nameof(contextHash));

        return new RecommendationContextSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RemediationCaseId = remediationCaseId,
            ContextJson = contextJson,
            ContextHash = contextHash ?? string.Empty,
            CitationsJson = string.IsNullOrWhiteSpace(citationsJson) ? "[]" : citationsJson,
            GeneratedBy = generatedBy,
            GeneratedAt = DateTimeOffset.UtcNow,
        };
    }
}
