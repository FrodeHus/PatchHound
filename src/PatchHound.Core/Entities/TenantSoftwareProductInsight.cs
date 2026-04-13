namespace PatchHound.Core.Entities;

public class TenantSoftwareProductInsight
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SoftwareProductId { get; private set; }
    public string? Description { get; private set; }
    public string? SupplyChainEvidenceJson { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TenantSoftwareProductInsight() { }

    public static TenantSoftwareProductInsight Create(Guid tenantId, Guid softwareProductId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }
        if (softwareProductId == Guid.Empty)
        {
            throw new ArgumentException("SoftwareProductId is required.", nameof(softwareProductId));
        }

        return new TenantSoftwareProductInsight
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SoftwareProductId = softwareProductId,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateDescription(string? description)
    {
        if (description is not null && description.Length > 4096)
        {
            throw new ArgumentException("Description must be 4096 characters or fewer.", nameof(description));
        }
        Description = description;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateSupplyChainEvidence(string? evidenceJson)
    {
        SupplyChainEvidenceJson = evidenceJson;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
