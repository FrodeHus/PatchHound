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
    DashboardRiskChangeBriefDto RiskChangeBrief,
    int RecurringVulnerabilityCount,
    decimal RecurrenceRatePercent,
    List<RecurringVulnerabilityDto> TopRecurringVulnerabilities,
    List<RecurringAssetDto> TopRecurringAssets,
    List<DeviceGroupVulnerabilityDto> VulnerabilitiesByDeviceGroup,
    Dictionary<string, int> DeviceHealthBreakdown,
    Dictionary<string, int> DeviceOnboardingBreakdown,
    List<SlaComplianceTrendPointDto>? SlaComplianceTrend = null,
    MetricSparklinesDto? MetricSparklines = null,
    List<VulnerabilityAgeBucketDto>? VulnerabilityAgeBuckets = null,
    List<MttrBySeverityDto>? MttrBySeverity = null
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

public record DashboardRiskChangeBriefDto(
    int AppearedCount,
    int ResolvedCount,
    List<DashboardRiskChangeItemDto> Appeared,
    List<DashboardRiskChangeItemDto> Resolved,
    string? AiSummary
);

public record DashboardRiskChangeItemDto(
    Guid TenantVulnerabilityId,
    string ExternalId,
    string Title,
    string Severity,
    int AffectedAssetCount,
    DateTimeOffset ChangedAt
);

public record DashboardFilterQuery(
    int? MinAgeDays = null,
    string? Platform = null,
    string? DeviceGroup = null
);

public record DeviceGroupVulnerabilityDto(
    string DeviceGroupName,
    int Critical,
    int High,
    int Medium,
    int Low
);

public record HeatmapRowDto(
    string Label,
    int Critical,
    int High,
    int Medium,
    int Low
);

public record DashboardFilterOptionsDto(
    string[] Platforms,
    string[] DeviceGroups
);

public record SlaComplianceTrendPointDto(DateOnly Date, decimal Percent);

public record MetricSparklinesDto(
    List<int> CriticalBacklog,
    List<int> OverdueActions,
    List<int> HealthyTasks,
    List<int> OpenStatuses
);

public record VulnerabilityAgeBucketDto(
    string Bucket,
    int Count,
    int Critical,
    int High,
    int Medium,
    int Low
);

public record MttrBySeverityDto(
    string Severity,
    decimal Days,
    decimal? PreviousDays
);

public record BurndownPointDto(DateOnly Date, int Discovered, int Resolved, int NetOpen);

public record BurndownTrendDto(List<BurndownPointDto> Items);

public record TrendDataDto(List<TrendItem> Items);

public record TrendItem(DateOnly Date, string Severity, int Count);
