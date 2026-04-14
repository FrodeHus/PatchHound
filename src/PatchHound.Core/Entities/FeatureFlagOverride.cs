namespace PatchHound.Core.Entities;

public class FeatureFlagOverride
{
    public Guid Id { get; private set; }
    public string FlagName { get; private set; } = null!;
    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    // Navigation properties
    public Tenant? Tenant { get; private set; }
    public User? User { get; private set; }

    private FeatureFlagOverride() { }

    public static FeatureFlagOverride CreateTenantOverride(
        string flagName,
        Guid tenantId,
        bool isEnabled,
        DateTimeOffset? expiresAt = null
    )
    {
        return new FeatureFlagOverride
        {
            Id = Guid.NewGuid(),
            FlagName = flagName,
            TenantId = tenantId,
            UserId = null,
            IsEnabled = isEnabled,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
        };
    }

    public static FeatureFlagOverride CreateUserOverride(
        string flagName,
        Guid userId,
        bool isEnabled,
        DateTimeOffset? expiresAt = null
    )
    {
        return new FeatureFlagOverride
        {
            Id = Guid.NewGuid(),
            FlagName = flagName,
            TenantId = null,
            UserId = userId,
            IsEnabled = isEnabled,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
        };
    }

    public void Update(bool isEnabled, DateTimeOffset? expiresAt)
    {
        IsEnabled = isEnabled;
        ExpiresAt = expiresAt;
    }
}
