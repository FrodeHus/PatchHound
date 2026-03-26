using System.Text.Json;
using PatchHound.Core.Models;

namespace PatchHound.Core.Entities;

public class TeamMembershipRule
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TeamId { get; private set; }
    public bool Enabled { get; private set; }
    public string FilterDefinition { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastExecutedAt { get; private set; }
    public int? LastMatchCount { get; private set; }

    public Team Team { get; private set; } = null!;

    private TeamMembershipRule() { }

    public static TeamMembershipRule Create(Guid tenantId, Guid teamId, FilterNode filter)
    {
        var now = DateTimeOffset.UtcNow;
        return new TeamMembershipRule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TeamId = teamId,
            Enabled = true,
            FilterDefinition = JsonSerializer.Serialize(filter, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(FilterNode filter, bool enabled)
    {
        FilterDefinition = JsonSerializer.Serialize(filter, JsonOptions);
        Enabled = enabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordExecution(int matchCount)
    {
        LastExecutedAt = DateTimeOffset.UtcNow;
        LastMatchCount = matchCount;
    }

    public FilterNode ParseFilter() =>
        JsonSerializer.Deserialize<FilterNode>(FilterDefinition, JsonOptions)!;
}
