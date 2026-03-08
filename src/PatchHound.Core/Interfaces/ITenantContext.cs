namespace PatchHound.Core.Interfaces;

public interface ITenantContext
{
    Guid? CurrentTenantId { get; }
    IReadOnlyList<Guid> AccessibleTenantIds { get; }
    Guid CurrentUserId { get; }
    bool IsSystemContext { get; }
    bool HasAccessToTenant(Guid tenantId);
    IReadOnlyList<string> GetRolesForTenant(Guid tenantId);
}
