using PatchHound.Core.Models;

namespace PatchHound.Core.Interfaces;

public interface IDeviceRuleEvaluationService
{
    Task EvaluateRulesAsync(Guid tenantId, CancellationToken ct);
    Task EvaluateCriticalityForDeviceAsync(Guid tenantId, Guid deviceId, CancellationToken ct);
    Task<DeviceRulePreviewResult> PreviewFilterAsync(Guid tenantId, FilterNode filter, CancellationToken ct);
}

public record DeviceRulePreviewResult(int Count, List<DevicePreviewItem> Samples);
public record DevicePreviewItem(Guid Id, string Name);
