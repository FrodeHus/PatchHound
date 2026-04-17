namespace PatchHound.Core.Entities;

public class CloudApplicationCredentialMetadata
{
    public Guid Id { get; private set; }
    public Guid CloudApplicationId { get; private set; }
    public Guid TenantId { get; private set; }
    public string ExternalId { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public string? DisplayName { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public CloudApplication? Application { get; private set; }

    private CloudApplicationCredentialMetadata() { }

    public static CloudApplicationCredentialMetadata Create(
        Guid cloudApplicationId,
        Guid tenantId,
        string externalId,
        string type,
        string? displayName,
        DateTimeOffset expiresAt
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CloudApplicationId = cloudApplicationId,
            TenantId = tenantId,
            ExternalId = externalId,
            Type = type,
            DisplayName = displayName,
            ExpiresAt = expiresAt,
        };
}
