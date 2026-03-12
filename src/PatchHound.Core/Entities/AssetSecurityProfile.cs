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
    public CvssModifiedAttackVector ModifiedAttackVector { get; private set; }
    public CvssModifiedAttackComplexity ModifiedAttackComplexity { get; private set; }
    public CvssModifiedPrivilegesRequired ModifiedPrivilegesRequired { get; private set; }
    public CvssModifiedUserInteraction ModifiedUserInteraction { get; private set; }
    public CvssModifiedScope ModifiedScope { get; private set; }
    public CvssModifiedImpact ModifiedConfidentialityImpact { get; private set; }
    public CvssModifiedImpact ModifiedIntegrityImpact { get; private set; }
    public CvssModifiedImpact ModifiedAvailabilityImpact { get; private set; }
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
        SecurityRequirementLevel availabilityRequirement,
        CvssModifiedAttackVector? modifiedAttackVector = null,
        CvssModifiedAttackComplexity modifiedAttackComplexity = CvssModifiedAttackComplexity.NotDefined,
        CvssModifiedPrivilegesRequired modifiedPrivilegesRequired = CvssModifiedPrivilegesRequired.NotDefined,
        CvssModifiedUserInteraction modifiedUserInteraction = CvssModifiedUserInteraction.NotDefined,
        CvssModifiedScope modifiedScope = CvssModifiedScope.NotDefined,
        CvssModifiedImpact modifiedConfidentialityImpact = CvssModifiedImpact.NotDefined,
        CvssModifiedImpact modifiedIntegrityImpact = CvssModifiedImpact.NotDefined,
        CvssModifiedImpact modifiedAvailabilityImpact = CvssModifiedImpact.NotDefined
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
            ModifiedAttackVector = modifiedAttackVector ?? MapInternetReachabilityToModifiedAttackVector(internetReachability),
            ModifiedAttackComplexity = modifiedAttackComplexity,
            ModifiedPrivilegesRequired = modifiedPrivilegesRequired,
            ModifiedUserInteraction = modifiedUserInteraction,
            ModifiedScope = modifiedScope,
            ModifiedConfidentialityImpact = modifiedConfidentialityImpact,
            ModifiedIntegrityImpact = modifiedIntegrityImpact,
            ModifiedAvailabilityImpact = modifiedAvailabilityImpact,
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
        SecurityRequirementLevel availabilityRequirement,
        CvssModifiedAttackVector? modifiedAttackVector = null,
        CvssModifiedAttackComplexity modifiedAttackComplexity = CvssModifiedAttackComplexity.NotDefined,
        CvssModifiedPrivilegesRequired modifiedPrivilegesRequired = CvssModifiedPrivilegesRequired.NotDefined,
        CvssModifiedUserInteraction modifiedUserInteraction = CvssModifiedUserInteraction.NotDefined,
        CvssModifiedScope modifiedScope = CvssModifiedScope.NotDefined,
        CvssModifiedImpact modifiedConfidentialityImpact = CvssModifiedImpact.NotDefined,
        CvssModifiedImpact modifiedIntegrityImpact = CvssModifiedImpact.NotDefined,
        CvssModifiedImpact modifiedAvailabilityImpact = CvssModifiedImpact.NotDefined
    )
    {
        Name = name;
        Description = description;
        EnvironmentClass = environmentClass;
        InternetReachability = internetReachability;
        ConfidentialityRequirement = confidentialityRequirement;
        IntegrityRequirement = integrityRequirement;
        AvailabilityRequirement = availabilityRequirement;
        ModifiedAttackVector = modifiedAttackVector ?? MapInternetReachabilityToModifiedAttackVector(internetReachability);
        ModifiedAttackComplexity = modifiedAttackComplexity;
        ModifiedPrivilegesRequired = modifiedPrivilegesRequired;
        ModifiedUserInteraction = modifiedUserInteraction;
        ModifiedScope = modifiedScope;
        ModifiedConfidentialityImpact = modifiedConfidentialityImpact;
        ModifiedIntegrityImpact = modifiedIntegrityImpact;
        ModifiedAvailabilityImpact = modifiedAvailabilityImpact;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static CvssModifiedAttackVector MapInternetReachabilityToModifiedAttackVector(
        InternetReachability internetReachability
    )
    {
        return internetReachability switch
        {
            InternetReachability.AdjacentOnly => CvssModifiedAttackVector.Adjacent,
            InternetReachability.LocalOnly => CvssModifiedAttackVector.Local,
            _ => CvssModifiedAttackVector.Network,
        };
    }
}
