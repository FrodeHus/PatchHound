namespace Vigil.Core.Entities;

public class User
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string EntraObjectId { get; private set; } = null!;

    private readonly List<UserTenantRole> _tenantRoles = [];
    public IReadOnlyCollection<UserTenantRole> TenantRoles => _tenantRoles.AsReadOnly();

    private User() { }

    public static User Create(string email, string displayName, string entraObjectId)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            EntraObjectId = entraObjectId,
        };
    }
}
