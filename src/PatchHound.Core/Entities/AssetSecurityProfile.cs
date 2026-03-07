using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class AssetSecurityProfile
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public EnvironmentClass EnvironmentClass { get; private set; }
    public InternetReachability InternetReachability { get; private set; }
    public SecurityRequirementLevel ConfidentialityRequirement { get; private set; }
    public SecurityRequirementLevel IntegrityRequirement { get; private set; }
    public SecurityRequirementLevel AvailabilityRequirement { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private AssetSecurityProfile() { }

    public static AssetSecurityProfile Create(
        Guid tenantId,
        string name,
        string? description,
        EnvironmentClass environmentClass,
        InternetReachability internetReachability,
        SecurityRequirementLevel confidentialityRequirement,
        SecurityRequirementLevel integrityRequirement,
        SecurityRequirementLevel availabilityRequirement
    )
    {
        var now = DateTimeOffset.UtcNow;

        return new AssetSecurityProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Description = description,
            EnvironmentClass = environmentClass,
            InternetReachability = internetReachability,
            ConfidentialityRequirement = confidentialityRequirement,
            IntegrityRequirement = integrityRequirement,
            AvailabilityRequirement = availabilityRequirement,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(
        string name,
        string? description,
        EnvironmentClass environmentClass,
        InternetReachability internetReachability,
        SecurityRequirementLevel confidentialityRequirement,
        SecurityRequirementLevel integrityRequirement,
        SecurityRequirementLevel availabilityRequirement
    )
    {
        Name = name;
        Description = description;
        EnvironmentClass = environmentClass;
        InternetReachability = internetReachability;
        ConfidentialityRequirement = confidentialityRequirement;
        IntegrityRequirement = integrityRequirement;
        AvailabilityRequirement = availabilityRequirement;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
