namespace Vigil.Core.Interfaces;

public interface ITenantContext
{
    Guid? CurrentTenantId { get; }
    IReadOnlyList<Guid> AccessibleTenantIds { get; }
    Guid CurrentUserId { get; }
}
