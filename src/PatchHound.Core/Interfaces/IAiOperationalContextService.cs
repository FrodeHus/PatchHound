using PatchHound.Core.Models.OperationalContext;

namespace PatchHound.Core.Interfaces;

public interface IAiOperationalContextService
{
    Task<AiOperationalContextResult> BuildForRemediationCaseAsync(
        Guid tenantId,
        Guid remediationCaseId,
        AiOperationalContextOptions options,
        CancellationToken ct);

    Task<AiOperationalContextResult> BuildForVulnerabilityAsync(
        Guid tenantId,
        Guid vulnerabilityId,
        AiOperationalContextOptions options,
        CancellationToken ct);
}
