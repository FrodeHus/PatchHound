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
        string content,
        string inputHash,
        string providerType,
        string profileName,
        string model
    )
    {
        RemediationAiSummaryContent = content.Trim();
        RemediationAiSummaryInputHash = inputHash.Trim();
        RemediationAiSummaryProviderType = providerType.Trim();
        RemediationAiSummaryProfileName = profileName.Trim();
        RemediationAiSummaryModel = model.Trim();
        RemediationAiSummaryGeneratedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearRemediationAiSummary()
    {
        RemediationAiSummaryContent = string.Empty;
        RemediationAiSummaryInputHash = string.Empty;
        RemediationAiSummaryProviderType = string.Empty;
        RemediationAiSummaryProfileName = string.Empty;
        RemediationAiSummaryModel = string.Empty;
        RemediationAiSummaryGeneratedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
