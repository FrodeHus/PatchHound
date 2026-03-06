using Vigil.Core.Enums;

namespace Vigil.Core.Entities;

public class UserTenantRole
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public RoleName Role { get; private set; }

    public User User { get; private set; } = null!;
    public Tenant Tenant { get; private set; } = null!;

    private UserTenantRole() { }

    public static UserTenantRole Create(Guid userId, Guid tenantId, RoleName role)
    {
        return new UserTenantRole
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            Role = role,
        };
    }
}
