namespace PatchHound.Api.Models.Dashboard;

public record DashboardSummaryDto(
    decimal ExposureScore,
    Dictionary<string, int> VulnerabilitiesBySeverity,
    Dictionary<string, int> VulnerabilitiesByStatus,
    decimal SlaCompliancePercent,
    int OverdueTaskCount,
    int TotalTaskCount,
    decimal AverageRemediationDays,
    List<TopVulnerabilityDto> TopCriticalVulnerabilities,
    int RecurringVulnerabilityCount,
    decimal RecurrenceRatePercent,
    List<RecurringVulnerabilityDto> TopRecurringVulnerabilities,
    List<RecurringAssetDto> TopRecurringAssets
);

public record TopVulnerabilityDto(
    Guid Id,
    string ExternalId,
    string Title,
    string Severity,
    decimal? CvssScore,
    int AffectedAssetCount,
    int DaysSincePublished
);

public record RecurringVulnerabilityDto(
    Guid Id,
    string ExternalId,
    string Title,
    int EpisodeCount,
    int ReappearanceCount
);

public record RecurringAssetDto(
    Guid AssetId,
    string Name,
    string AssetType,
    int RecurringVulnerabilityCount
);

public record TrendDataDto(List<TrendItem> Items);

public record TrendItem(DateOnly Date, string Severity, int Count);
