using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class AnalystRecommendation
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RemediationCaseId { get; private set; }
    public Guid? RemediationWorkflowId { get; private set; }
    public Guid? VulnerabilityId { get; private set; }
    public RemediationOutcome RecommendedOutcome { get; private set; }
    public string Rationale { get; private set; } = null!;
    public string? PriorityOverride { get; private set; }
    public Guid AnalystId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public RemediationCase RemediationCase { get; private set; } = null!;
    public RemediationWorkflow? RemediationWorkflow { get; private set; }

    private AnalystRecommendation() { }

    public static AnalystRecommendation Create(
        Guid tenantId,
        Guid remediationCaseId,
        RemediationOutcome recommendedOutcome,
        string rationale,
        Guid analystId,
        Guid? vulnerabilityId = null,
        string? priorityOverride = null)
    {
        if (string.IsNullOrWhiteSpace(rationale))
            throw new ArgumentException("Rationale is required.");

        return new AnalystRecommendation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RemediationCaseId = remediationCaseId,
            VulnerabilityId = vulnerabilityId,
            RecommendedOutcome = recommendedOutcome,
            Rationale = rationale,
            PriorityOverride = priorityOverride,
            AnalystId = analystId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void AttachToWorkflow(Guid remediationWorkflowId)
    {
        RemediationWorkflowId = remediationWorkflowId;
    }

    public void Update(
        RemediationOutcome recommendedOutcome,
        string rationale,
        Guid analystId,
        Guid? vulnerabilityId = null,
        string? priorityOverride = null)
    {
        if (string.IsNullOrWhiteSpace(rationale))
            throw new ArgumentException("Rationale is required.");

        RecommendedOutcome = recommendedOutcome;
        Rationale = rationale;
        AnalystId = analystId;
        VulnerabilityId = vulnerabilityId;
        PriorityOverride = priorityOverride;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
