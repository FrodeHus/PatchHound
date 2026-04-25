namespace PatchHound.Core.Entities;

public class StoredCredentialTenant
{
    public Guid StoredCredentialId { get; private set; }
    public Guid TenantId { get; private set; }

    public StoredCredential StoredCredential { get; private set; } = null!;
    public Tenant Tenant { get; private set; } = null!;

    private StoredCredentialTenant() { }

    public static StoredCredentialTenant Create(Guid storedCredentialId, Guid tenantId)
    {
        if (storedCredentialId == Guid.Empty) throw new ArgumentException(nameof(storedCredentialId));
        if (tenantId == Guid.Empty) throw new ArgumentException(nameof(tenantId));

        return new StoredCredentialTenant
        {
            StoredCredentialId = storedCredentialId,
            TenantId = tenantId,
        };
    }
}
