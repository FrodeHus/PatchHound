namespace PatchHound.Core.Models;

public record RiskChangeBriefSummaryInput(
    int AppearedCount,
    int ResolvedCount,
    IReadOnlyList<RiskChangeBriefSummaryItemInput> Appeared,
    IReadOnlyList<RiskChangeBriefSummaryItemInput> Resolved
);

public record RiskChangeBriefSummaryItemInput(
    string ExternalId,
    string Title,
    string Severity,
    int AffectedAssetCount,
    DateTimeOffset ChangedAt
);
