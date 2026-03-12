namespace PatchHound.Core.Entities;

public static class IngestionRunStatuses
{
    public const string Staging = "Staging";
    public const string MergePending = "MergePending";
    public const string Merging = "Merging";
    public const string Succeeded = "Succeeded";
    public const string FailedRecoverable = "FailedRecoverable";
    public const string FailedTerminal = "FailedTerminal";
}

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
    public int StagedVulnerabilityCount { get; private set; }
    public int StagedExposureCount { get; private set; }
    public int MergedExposureCount { get; private set; }
    public int OpenedProjectionCount { get; private set; }
    public int ResolvedProjectionCount { get; private set; }
    public int StagedAssetCount { get; private set; }
    public int MergedAssetCount { get; private set; }
    public int StagedSoftwareLinkCount { get; private set; }
    public int ResolvedSoftwareLinkCount { get; private set; }
    public int InstallationsCreated { get; private set; }
    public int InstallationsTouched { get; private set; }
    public int InstallationEpisodesOpened { get; private set; }
    public int InstallationEpisodesSeen { get; private set; }
    public int StaleInstallationsMarked { get; private set; }
    public int InstallationsRemoved { get; private set; }
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
            Status = IngestionRunStatuses.Staging,
        };
    }

    public void UpdateStatus(string status)
    {
        Status = status;
    }

    public void CompleteSucceeded(
        DateTimeOffset completedAt,
        int fetchedVulnerabilityCount,
        int fetchedAssetCount,
        int fetchedSoftwareInstallationCount,
        int stagedVulnerabilityCount,
        int stagedExposureCount,
        int mergedExposureCount,
        int openedProjectionCount,
        int resolvedProjectionCount,
        int stagedAssetCount,
        int mergedAssetCount,
        int stagedSoftwareLinkCount,
        int resolvedSoftwareLinkCount,
        int installationsCreated,
        int installationsTouched,
        int installationEpisodesOpened,
        int installationEpisodesSeen,
        int staleInstallationsMarked,
        int installationsRemoved
    )
    {
        CompletedAt = completedAt;
        Status = IngestionRunStatuses.Succeeded;
        FetchedVulnerabilityCount = fetchedVulnerabilityCount;
        FetchedAssetCount = fetchedAssetCount;
        FetchedSoftwareInstallationCount = fetchedSoftwareInstallationCount;
        StagedVulnerabilityCount = stagedVulnerabilityCount;
        StagedExposureCount = stagedExposureCount;
        MergedExposureCount = mergedExposureCount;
        OpenedProjectionCount = openedProjectionCount;
        ResolvedProjectionCount = resolvedProjectionCount;
        StagedAssetCount = stagedAssetCount;
        MergedAssetCount = mergedAssetCount;
        StagedSoftwareLinkCount = stagedSoftwareLinkCount;
        ResolvedSoftwareLinkCount = resolvedSoftwareLinkCount;
        InstallationsCreated = installationsCreated;
        InstallationsTouched = installationsTouched;
        InstallationEpisodesOpened = installationEpisodesOpened;
        InstallationEpisodesSeen = installationEpisodesSeen;
        StaleInstallationsMarked = staleInstallationsMarked;
        InstallationsRemoved = installationsRemoved;
        Error = string.Empty;
    }

    public void CompleteFailed(
        DateTimeOffset completedAt,
        string error,
        string failureStatus,
        int fetchedVulnerabilityCount,
        int fetchedAssetCount,
        int fetchedSoftwareInstallationCount,
        int stagedVulnerabilityCount,
        int stagedExposureCount,
        int mergedExposureCount,
        int openedProjectionCount,
        int resolvedProjectionCount,
        int stagedAssetCount,
        int mergedAssetCount,
        int stagedSoftwareLinkCount,
        int resolvedSoftwareLinkCount,
        int installationsCreated,
        int installationsTouched,
        int installationEpisodesOpened,
        int installationEpisodesSeen,
        int staleInstallationsMarked,
        int installationsRemoved
    )
    {
        CompletedAt = completedAt;
        Status = failureStatus;
        FetchedVulnerabilityCount = fetchedVulnerabilityCount;
        FetchedAssetCount = fetchedAssetCount;
        FetchedSoftwareInstallationCount = fetchedSoftwareInstallationCount;
        StagedVulnerabilityCount = stagedVulnerabilityCount;
        StagedExposureCount = stagedExposureCount;
        MergedExposureCount = mergedExposureCount;
        OpenedProjectionCount = openedProjectionCount;
        ResolvedProjectionCount = resolvedProjectionCount;
        StagedAssetCount = stagedAssetCount;
        MergedAssetCount = mergedAssetCount;
        StagedSoftwareLinkCount = stagedSoftwareLinkCount;
        ResolvedSoftwareLinkCount = resolvedSoftwareLinkCount;
        InstallationsCreated = installationsCreated;
        InstallationsTouched = installationsTouched;
        InstallationEpisodesOpened = installationEpisodesOpened;
        InstallationEpisodesSeen = installationEpisodesSeen;
        StaleInstallationsMarked = staleInstallationsMarked;
        InstallationsRemoved = installationsRemoved;
        Error = error;
    }
}
