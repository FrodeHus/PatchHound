using PatchHound.Core.Models;

namespace PatchHound.Core.Interfaces;

public interface IAssetRuleEvaluationService
{
    Task EvaluateRulesAsync(Guid tenantId, CancellationToken ct);
    Task<AssetRulePreviewResult> PreviewFilterAsync(Guid tenantId, FilterNode filter, CancellationToken ct);
}

public record AssetRulePreviewResult(int Count, List<AssetPreviewItem> Samples);
public record AssetPreviewItem(Guid Id, string Name, string AssetType);
