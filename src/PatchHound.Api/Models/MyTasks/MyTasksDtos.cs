namespace PatchHound.Api.Models.MyTasks;

public record MyTasksPageDto(
    IReadOnlyList<MyTaskBucketDto> Sections
);

public record MyTaskBucketDto(
    string Bucket,
    IReadOnlyList<MyTaskListItemDto> Items,
    int Page,
    int PageSize,
    bool HasMore
);

public record MyTaskListItemDto(
    Guid RemediationCaseId,
    string SoftwareName,
    string Criticality,
    string? Outcome,
    string? ApprovalStatus,
    int TotalVulnerabilities,
    int CriticalCount,
    int HighCount,
    double? RiskScore,
    string? RiskBand,
    string? SlaStatus,
    DateTimeOffset? SlaDueDate,
    int AffectedDeviceCount,
    string? SoftwareOwnerTeamName,
    string SoftwareOwnerAssignmentSource,
    string? WorkflowStage
);

public record MyTasksQuery(
    int Page = 1,
    int PageSize = 25,
    int RecommendationPage = 1,
    int DecisionPage = 1,
    int ApprovalPage = 1
)
{
    private const int MaxPageSize = 100;
    public int BoundedPageSize => Math.Clamp(PageSize, 1, MaxPageSize);

    public int PageFor(string bucket) =>
        Math.Max(bucket switch
        {
            MyTaskBuckets.Recommendation => RecommendationPage,
            MyTaskBuckets.Decision => DecisionPage,
            MyTaskBuckets.Approval => ApprovalPage,
            _ => Page,
        }, 1);
}

public static class MyTaskBuckets
{
    public const string Recommendation = "recommendation";
    public const string Decision = "decision";
    public const string Approval = "approval";
}
