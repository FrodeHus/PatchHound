using Vigil.Core.Enums;

namespace Vigil.Core.Entities;

public class Campaign
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public CampaignStatus Status { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<CampaignVulnerability> _vulnerabilities = [];
    public IReadOnlyCollection<CampaignVulnerability> Vulnerabilities =>
        _vulnerabilities.AsReadOnly();

    private Campaign() { }

    public static Campaign Create(
        Guid tenantId,
        string name,
        Guid createdBy,
        string? description = null
    )
    {
        return new Campaign
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Description = description,
            Status = CampaignStatus.Active,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string? name, string? description)
    {
        if (name is not null)
            Name = name;
        if (description is not null)
            Description = description;
    }

    public void Close()
    {
        Status = CampaignStatus.Closed;
    }

    public void AddVulnerability(Guid vulnerabilityId)
    {
        if (_vulnerabilities.Any(v => v.VulnerabilityId == vulnerabilityId))
            return;

        _vulnerabilities.Add(CampaignVulnerability.Create(Id, vulnerabilityId));
    }
}
