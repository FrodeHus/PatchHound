namespace PatchHound.Core.Entities.AuthenticatedScans;

public class ConnectionProfile
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Kind { get; private set; } = "ssh";
    public string SshHost { get; private set; } = string.Empty;
    public int SshPort { get; private set; } = 22;
    public string SshUsername { get; private set; } = string.Empty;
    public string AuthMethod { get; private set; } = "password";
    public string SecretRef { get; private set; } = string.Empty;
    public string? HostKeyFingerprint { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ConnectionProfile() { }

    public static ConnectionProfile Create(
        Guid tenantId,
        string name,
        string description,
        string sshHost,
        int sshPort,
        string sshUsername,
        string authMethod,
        string secretRef,
        string? hostKeyFingerprint)
    {
        if (authMethod is not ("password" or "privateKey"))
            throw new ArgumentException("authMethod must be 'password' or 'privateKey'", nameof(authMethod));
        var now = DateTimeOffset.UtcNow;
        return new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Description = description.Trim(),
            Kind = "ssh",
            SshHost = sshHost.Trim(),
            SshPort = sshPort,
            SshUsername = sshUsername.Trim(),
            AuthMethod = authMethod,
            SecretRef = secretRef,
            HostKeyFingerprint = string.IsNullOrWhiteSpace(hostKeyFingerprint) ? null : hostKeyFingerprint.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(
        string name,
        string description,
        string sshHost,
        int sshPort,
        string sshUsername,
        string authMethod,
        string? hostKeyFingerprint)
    {
        if (authMethod is not ("password" or "privateKey"))
            throw new ArgumentException("authMethod must be 'password' or 'privateKey'", nameof(authMethod));
        Name = name.Trim();
        Description = description.Trim();
        SshHost = sshHost.Trim();
        SshPort = sshPort;
        SshUsername = sshUsername.Trim();
        AuthMethod = authMethod;
        HostKeyFingerprint = string.IsNullOrWhiteSpace(hostKeyFingerprint) ? null : hostKeyFingerprint.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetSecretRef(string secretRef)
    {
        SecretRef = secretRef;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
