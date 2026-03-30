namespace PatchHound.Core.Entities;

public class TenantSoftware
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? SnapshotId { get; private set; }
    public Guid NormalizedSoftwareId { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public string RemediationAiSummaryContent { get; private set; } = string.Empty;
    public string RemediationAiSummaryInputHash { get; private set; } = string.Empty;
    public string RemediationAiSummaryProviderType { get; private set; } = string.Empty;
    public string RemediationAiSummaryProfileName { get; private set; } = string.Empty;
    public string RemediationAiSummaryModel { get; private set; } = string.Empty;
    public string RemediationAiOwnerRecommendationContent { get; private set; } = string.Empty;
    public string RemediationAiAnalystAssessmentContent { get; private set; } = string.Empty;
    public string RemediationAiExceptionRecommendationContent { get; private set; } = string.Empty;
    public string RemediationAiRecommendedOutcome { get; private set; } = string.Empty;
    public string RemediationAiRecommendedPriority { get; private set; } = string.Empty;
    public string RemediationAiReviewStatus { get; private set; } = string.Empty;
    public Guid? RemediationAiReviewedBy { get; private set; }
    public DateTimeOffset? RemediationAiReviewedAt { get; private set; }
    public DateTimeOffset? RemediationAiSummaryGeneratedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public NormalizedSoftware NormalizedSoftware { get; private set; } = null!;

    private TenantSoftware() { }

    public static TenantSoftware Create(
        Guid tenantId,
        Guid? snapshotId,
        Guid normalizedSoftwareId,
        DateTimeOffset firstSeenAt,
        DateTimeOffset lastSeenAt
    )
    {
        return new TenantSoftware
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SnapshotId = snapshotId,
            NormalizedSoftwareId = normalizedSoftwareId,
            FirstSeenAt = firstSeenAt,
            LastSeenAt = lastSeenAt,
            CreatedAt = firstSeenAt,
            UpdatedAt = lastSeenAt,
        };
    }

    public void UpdateObservationWindow(DateTimeOffset firstSeenAt, DateTimeOffset lastSeenAt)
    {
        FirstSeenAt = firstSeenAt;
        LastSeenAt = lastSeenAt;
        UpdatedAt = lastSeenAt;
    }

    public void AssignSnapshot(Guid? snapshotId)
    {
        SnapshotId = snapshotId;
    }

    public void StoreRemediationAiSummary(
        string executiveSummary,
        string ownerRecommendation,
        string analystAssessment,
        string exceptionRecommendation,
        string recommendedOutcome,
        string recommendedPriority,
        string inputHash,
        string providerType,
        string profileName,
        string model
    )
    {
        RemediationAiSummaryContent = executiveSummary.Trim();
        RemediationAiOwnerRecommendationContent = ownerRecommendation.Trim();
        RemediationAiAnalystAssessmentContent = analystAssessment.Trim();
        RemediationAiExceptionRecommendationContent = exceptionRecommendation.Trim();
        RemediationAiRecommendedOutcome = recommendedOutcome.Trim();
        RemediationAiRecommendedPriority = recommendedPriority.Trim();
        RemediationAiSummaryInputHash = inputHash.Trim();
        RemediationAiSummaryProviderType = providerType.Trim();
        RemediationAiSummaryProfileName = profileName.Trim();
        RemediationAiSummaryModel = model.Trim();
        RemediationAiReviewStatus = string.Empty;
        RemediationAiReviewedBy = null;
        RemediationAiReviewedAt = null;
        RemediationAiSummaryGeneratedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkRemediationAiReviewed(string reviewStatus, Guid reviewedBy)
    {
        if (string.IsNullOrWhiteSpace(reviewStatus))
            throw new ArgumentException("Review status is required.", nameof(reviewStatus));

        RemediationAiReviewStatus = reviewStatus.Trim();
        RemediationAiReviewedBy = reviewedBy;
        RemediationAiReviewedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearRemediationAiSummary()
    {
        RemediationAiSummaryContent = string.Empty;
        RemediationAiOwnerRecommendationContent = string.Empty;
        RemediationAiAnalystAssessmentContent = string.Empty;
        RemediationAiExceptionRecommendationContent = string.Empty;
        RemediationAiRecommendedOutcome = string.Empty;
        RemediationAiRecommendedPriority = string.Empty;
        RemediationAiSummaryInputHash = string.Empty;
        RemediationAiSummaryProviderType = string.Empty;
        RemediationAiSummaryProfileName = string.Empty;
        RemediationAiSummaryModel = string.Empty;
        RemediationAiReviewStatus = string.Empty;
        RemediationAiReviewedBy = null;
        RemediationAiReviewedAt = null;
        RemediationAiSummaryGeneratedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
