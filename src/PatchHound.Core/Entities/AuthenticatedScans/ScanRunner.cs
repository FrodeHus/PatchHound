namespace PatchHound.Core.Entities.AuthenticatedScans;

public class ScanRunner
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string SecretHash { get; private set; } = string.Empty;
    public DateTimeOffset? LastSeenAt { get; private set; }
    public string Version { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ScanRunner() { }

    public static ScanRunner Create(Guid tenantId, string name, string description, string secretHash)
    {
        var now = DateTimeOffset.UtcNow;
        return new ScanRunner
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Description = description.Trim(),
            SecretHash = secretHash,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string name, string description, bool enabled)
    {
        Name = name.Trim();
        Description = description.Trim();
        Enabled = enabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RotateSecret(string newHash)
    {
        SecretHash = newHash;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordHeartbeat(string version, DateTimeOffset at)
    {
        Version = version.Trim();
        LastSeenAt = at;
    }
}
