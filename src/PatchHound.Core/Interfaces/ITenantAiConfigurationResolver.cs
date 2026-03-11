using PatchHound.Core.Common;
using PatchHound.Core.Models;

namespace PatchHound.Core.Interfaces;

public interface ITenantAiConfigurationResolver
{
    Task<Result<TenantAiProfileResolved>> ResolveDefaultAsync(Guid tenantId, CancellationToken ct);
    Task<Result<TenantAiProfileResolved>> ResolveByIdAsync(
        Guid tenantId,
        Guid profileId,
        CancellationToken ct
    );
}
