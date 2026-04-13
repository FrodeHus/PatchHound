using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class SecurityProfile
{
    public const int NameMaxLength = 256;
    public const int DescriptionMaxLength = 2048;

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

    private SecurityProfile() { }

    public static SecurityProfile Create(
        Guid tenantId,
        string name,
        string? description,
        EnvironmentClass environmentClass = EnvironmentClass.Workstation,
        InternetReachability internetReachability = InternetReachability.InternalNetwork,
        SecurityRequirementLevel confidentialityRequirement = SecurityRequirementLevel.Low,
        SecurityRequirementLevel integrityRequirement = SecurityRequirementLevel.Low,
        SecurityRequirementLevel availabilityRequirement = SecurityRequirementLevel.Low,
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
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }

        var (normalizedName, normalizedDescription) = NormalizeAndValidate(name, description);

        var now = DateTimeOffset.UtcNow;

        return new SecurityProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = normalizedName,
            Description = normalizedDescription,
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
        var (normalizedName, normalizedDescription) = NormalizeAndValidate(name, description);

        Name = normalizedName;
        Description = normalizedDescription;
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

    private static (string name, string? description) NormalizeAndValidate(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        var normalizedName = name.Trim();
        var normalizedDescription = description?.Trim();

        if (normalizedName.Length > NameMaxLength)
        {
            throw new ArgumentException(
                $"Name must be {NameMaxLength} characters or fewer.",
                nameof(name));
        }
        if (normalizedDescription is not null && normalizedDescription.Length > DescriptionMaxLength)
        {
            throw new ArgumentException(
                $"Description must be {DescriptionMaxLength} characters or fewer.",
                nameof(description));
        }

        return (normalizedName, normalizedDescription);
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
