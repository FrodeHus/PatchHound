namespace PatchHound.Core.Entities;

public class CloudApplication
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SourceSystemId { get; private set; }
    public string ExternalId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool ActiveInTenant { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public ICollection<CloudApplicationCredentialMetadata> Credentials { get; private set; } =
        new List<CloudApplicationCredentialMetadata>();

    private CloudApplication() { }

    public static CloudApplication Create(
        Guid tenantId,
        Guid sourceSystemId,
        string externalId,
        string name,
        string? description
    )
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (sourceSystemId == Guid.Empty)
            throw new ArgumentException("SourceSystemId is required.", nameof(sourceSystemId));
        if (string.IsNullOrWhiteSpace(externalId))
            throw new ArgumentException("ExternalId is required.", nameof(externalId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        var now = DateTimeOffset.UtcNow;
        return new CloudApplication
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceSystemId = sourceSystemId,
            ExternalId = externalId.Trim(),
            Name = name.Trim(),
            Description = description?.Trim(),
            ActiveInTenant = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string name, string? description)
    {
        Name = name.Trim();
        Description = description?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetActiveInTenant(bool active)
    {
        ActiveInTenant = active;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
