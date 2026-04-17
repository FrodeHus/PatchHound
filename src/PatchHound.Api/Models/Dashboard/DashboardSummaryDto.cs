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
    List<UnhandledVulnerabilityDto> LatestUnhandledVulnerabilities,
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

public record UnhandledVulnerabilityDto(
    Guid Id,
    string ExternalId,
    string Title,
    string Severity,
    decimal? CvssScore,
    int AffectedAssetCount,
    int DaysSincePublished,
    DateTimeOffset LatestSeenAt
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
    Guid VulnerabilityId,
    string ExternalId,
    string Title,
    string Severity,
    int AffectedAssetCount,
    DateTimeOffset ChangedAt,
    Guid? RemediationCaseId = null
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
    int Low,
    decimal? CurrentRiskScore = null,
    int? AssetCount = null,
    int? OpenEpisodeCount = null
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

public record OwnerDashboardSummaryDto(
    int OwnedAssetCount,
    int AssetsNeedingAttention,
    int OpenActionCount,
    int OverdueActionCount,
    List<OwnerAssetSummaryDto> TopOwnedAssets,
    List<OwnerActionDto> Actions,
    List<OwnerCloudAppActionDto> CloudAppActions
);

public record OwnerAssetSummaryDto(
    Guid AssetId,
    string AssetName,
    string? DeviceGroupName,
    string Criticality,
    decimal? CurrentRiskScore,
    string? RiskBand,
    int OpenEpisodeCount,
    string? TopDriverTitle,
    string? TopDriverSummary,
    DateTimeOffset? LastSeenAt = null,
    int CriticalCount = 0,
    int HighCount = 0,
    int MediumCount = 0,
    int LowCount = 0
);

public record OwnerActionDto(
    Guid TenantSoftwareId,
    Guid VulnerabilityId,
    Guid? TaskId,
    string SoftwareName,
    string OwnerTeamName,
    string ExternalId,
    string Title,
    List<string> SoftwareNames,
    string OwnerSummary,
    string Severity,
    decimal? EpisodeRiskScore,
    string? EpisodeRiskBand,
    DateTimeOffset? DueDate,
    string ActionState
);

public record OwnerCloudAppActionDto(
    Guid CloudApplicationId,
    string AppName,
    string? AppId,
    string OwnerTeamName,
    int ExpiredCredentialCount,
    int ExpiringCredentialCount,
    DateTimeOffset? NearestExpiryAt
);

public record ApprovalAttentionTaskDto(
    Guid ApprovalTaskId,
    Guid RemediationDecisionId,
    Guid RemediationCaseId,
    string SoftwareName,
    string ApprovalType,
    string HighestSeverity,
    int VulnerabilityCount,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? MaintenanceWindowDate,
    DateTimeOffset CreatedAt,
    string AttentionState
);

public record ApprovedPolicyDecisionDto(
    Guid DecisionId,
    Guid RemediationCaseId,
    string SoftwareName,
    string Outcome,
    string? Justification,
    string HighestSeverity,
    int VulnerabilityCount,
    DateTimeOffset ApprovedAt,
    DateTimeOffset? ExpiryDate
);

public record SecurityManagerDashboardSummaryDto(
    List<ApprovedPolicyDecisionDto> RecentApprovedDecisions,
    List<ApprovalAttentionTaskDto> ApprovalTasksRequiringAttention
);

public record ApprovedPatchingTaskDto(
    Guid PatchingTaskId,
    Guid RemediationDecisionId,
    Guid RemediationCaseId,
    string SoftwareName,
    string OwnerTeamName,
    string HighestSeverity,
    int AffectedDeviceCount,
    DateTimeOffset ApprovedAt,
    DateTimeOffset DueDate,
    DateTimeOffset? MaintenanceWindowDate,
    string Status
);

public record DevicePatchDriftDto(
    Guid DeviceAssetId,
    string DeviceName,
    string Criticality,
    string HighestSeverity,
    int OldVulnerabilityCount,
    DateTimeOffset OldestPublishedDate
);

public record TechnicalManagerDashboardSummaryDto(
    int MissedMaintenanceWindowCount,
    List<ApprovedPatchingTaskDto> ApprovedPatchingTasks,
    List<DevicePatchDriftDto> DevicesWithAgedVulnerabilities,
    List<ApprovalAttentionTaskDto> ApprovalTasksRequiringAttention
);
