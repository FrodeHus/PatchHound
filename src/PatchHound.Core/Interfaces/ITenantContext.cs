using PatchHound.Core.Enums;

namespace PatchHound.Core.Interfaces;

public interface ITenantContext
{
    Guid? CurrentTenantId { get; }
    IReadOnlyList<Guid> AccessibleTenantIds { get; }
    Guid CurrentUserId { get; }
    bool IsSystemContext { get; }
    bool IsInternalUser { get; }
    UserAccessScope CurrentAccessScope { get; }
    bool HasAccessToTenant(Guid tenantId);
    IReadOnlyList<string> GetRolesForTenant(Guid tenantId);
}
