namespace PatchHound.Core.Entities;

public class ExecutiveDashboardBriefing
{
    public Guid TenantId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; private set; }
    public DateTimeOffset WindowStartedAt { get; private set; }
    public DateTimeOffset WindowEndedAt { get; private set; }
    public int HighCriticalAppearedCount { get; private set; }
    public int ResolvedCount { get; private set; }
    public bool UsedAi { get; private set; }

    private ExecutiveDashboardBriefing() { }

    public static ExecutiveDashboardBriefing Create(
        Guid tenantId,
        string content,
        DateTimeOffset generatedAt,
        DateTimeOffset windowStartedAt,
        DateTimeOffset windowEndedAt,
        int highCriticalAppearedCount,
        int resolvedCount,
        bool usedAi
    )
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        return new ExecutiveDashboardBriefing
        {
            TenantId = tenantId,
            Content = NormalizeContent(content),
            GeneratedAt = generatedAt,
            WindowStartedAt = windowStartedAt,
            WindowEndedAt = windowEndedAt,
            HighCriticalAppearedCount = Math.Max(0, highCriticalAppearedCount),
            ResolvedCount = Math.Max(0, resolvedCount),
            UsedAi = usedAi,
        };
    }

    public void Update(
        string content,
        DateTimeOffset generatedAt,
        DateTimeOffset windowStartedAt,
        DateTimeOffset windowEndedAt,
        int highCriticalAppearedCount,
        int resolvedCount,
        bool usedAi
    )
    {
        Content = NormalizeContent(content);
        GeneratedAt = generatedAt;
        WindowStartedAt = windowStartedAt;
        WindowEndedAt = windowEndedAt;
        HighCriticalAppearedCount = Math.Max(0, highCriticalAppearedCount);
        ResolvedCount = Math.Max(0, resolvedCount);
        UsedAi = usedAi;
    }

    private static string NormalizeContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Briefing content is required.", nameof(content));
        }

        return content.Trim();
    }
}
