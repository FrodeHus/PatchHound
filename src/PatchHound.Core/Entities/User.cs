namespace PatchHound.Core.Entities;

public class User
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string EntraObjectId { get; private set; } = null!;
    public string? Company { get; private set; }
    public bool IsEnabled { get; private set; }

    private readonly List<UserTenantRole> _tenantRoles = [];
    public IReadOnlyCollection<UserTenantRole> TenantRoles => _tenantRoles.AsReadOnly();

    private User() { }

    public static User Create(
        string email,
        string displayName,
        string entraObjectId,
        string? company = null
    )
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            EntraObjectId = entraObjectId,
            Company = string.IsNullOrWhiteSpace(company) ? null : company.Trim(),
            IsEnabled = true,
        };
    }

    public void UpdateProfile(string email, string displayName, string? company = null)
    {
        Email = email;
        DisplayName = displayName;
        Company = string.IsNullOrWhiteSpace(company) ? null : company.Trim();
    }

    public void SetEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
    }
}
