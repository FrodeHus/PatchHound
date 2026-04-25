namespace PatchHound.Core.Entities;

public class StoredCredential
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public bool IsGlobal { get; private set; }
    public string CredentialTenantId { get; private set; } = string.Empty;
    public string ClientId { get; private set; } = string.Empty;
    public string SecretRef { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public ICollection<StoredCredentialTenant> TenantScopes { get; private set; } =
        new List<StoredCredentialTenant>();

    private StoredCredential() { }

    public static StoredCredential Create(
        string name,
        string type,
        bool isGlobal,
        string credentialTenantId,
        string clientId,
        string secretRef,
        DateTimeOffset now,
        Guid? id = null
    )
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(nameof(name));
        if (string.IsNullOrWhiteSpace(type)) throw new ArgumentException(nameof(type));
        if (string.IsNullOrWhiteSpace(secretRef)) throw new ArgumentException(nameof(secretRef));

        return new StoredCredential
        {
            Id = id ?? Guid.NewGuid(),
            Name = name.Trim(),
            Type = type.Trim(),
            IsGlobal = isGlobal,
            CredentialTenantId = credentialTenantId.Trim(),
            ClientId = clientId.Trim(),
            SecretRef = secretRef.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(
        string name,
        bool isGlobal,
        string credentialTenantId,
        string clientId,
        DateTimeOffset now
    )
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(nameof(name));

        Name = name.Trim();
        IsGlobal = isGlobal;
        CredentialTenantId = credentialTenantId.Trim();
        ClientId = clientId.Trim();
        UpdatedAt = now;
    }

    public void ReplaceSecretRef(string secretRef, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secretRef)) throw new ArgumentException(nameof(secretRef));

        SecretRef = secretRef.Trim();
        UpdatedAt = now;
    }
}
