namespace PatchHound.Core.Models.OperationalContext;

public sealed class OperationalContextCitation
{
    public required string Key { get; init; }
    public required string EntityType { get; init; }
    public Guid EntityId { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Fact { get; init; } = string.Empty;

    /// <summary>Risk weight used for lowest-risk-first truncation. Higher = keep longer.</summary>
    public double RiskWeight { get; init; }
}
