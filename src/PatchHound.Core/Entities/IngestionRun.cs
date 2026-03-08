namespace PatchHound.Core.Entities;

public class IngestionRun
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string SourceKey { get; private set; } = string.Empty;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public int FetchedVulnerabilityCount { get; private set; }
    public int FetchedAssetCount { get; private set; }
    public int FetchedSoftwareInstallationCount { get; private set; }
    public string Error { get; private set; } = string.Empty;

    private IngestionRun() { }

    public static IngestionRun Start(Guid tenantId, string sourceKey, DateTimeOffset startedAt)
    {
        return new IngestionRun
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceKey = sourceKey,
            StartedAt = startedAt,
            Status = "Running",
        };
    }

    public void CompleteSucceeded(
        DateTimeOffset completedAt,
        int fetchedVulnerabilityCount,
        int fetchedAssetCount,
        int fetchedSoftwareInstallationCount
    )
    {
        CompletedAt = completedAt;
        Status = "Succeeded";
        FetchedVulnerabilityCount = fetchedVulnerabilityCount;
        FetchedAssetCount = fetchedAssetCount;
        FetchedSoftwareInstallationCount = fetchedSoftwareInstallationCount;
        Error = string.Empty;
    }

    public void CompleteFailed(
        DateTimeOffset completedAt,
        string error,
        int fetchedVulnerabilityCount,
        int fetchedAssetCount,
        int fetchedSoftwareInstallationCount
    )
    {
        CompletedAt = completedAt;
        Status = "Failed";
        FetchedVulnerabilityCount = fetchedVulnerabilityCount;
        FetchedAssetCount = fetchedAssetCount;
        FetchedSoftwareInstallationCount = fetchedSoftwareInstallationCount;
        Error = error;
    }
}
