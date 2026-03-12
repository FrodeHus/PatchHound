namespace PatchHound.Api.Models.SecurityProfiles;

public record SecurityProfileDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Description,
    string EnvironmentClass,
    string InternetReachability,
    string ConfidentialityRequirement,
    string IntegrityRequirement,
    string AvailabilityRequirement,
    string ModifiedAttackVector,
    string ModifiedAttackComplexity,
    string ModifiedPrivilegesRequired,
    string ModifiedUserInteraction,
    string ModifiedScope,
    string ModifiedConfidentialityImpact,
    string ModifiedIntegrityImpact,
    string ModifiedAvailabilityImpact,
    DateTimeOffset UpdatedAt
);

public record CreateSecurityProfileRequest(
    string Name,
    string? Description,
    string EnvironmentClass,
    string InternetReachability,
    string ConfidentialityRequirement,
    string IntegrityRequirement,
    string AvailabilityRequirement,
    string ModifiedAttackVector,
    string ModifiedAttackComplexity,
    string ModifiedPrivilegesRequired,
    string ModifiedUserInteraction,
    string ModifiedScope,
    string ModifiedConfidentialityImpact,
    string ModifiedIntegrityImpact,
    string ModifiedAvailabilityImpact
);

public record UpdateSecurityProfileRequest(
    string Name,
    string? Description,
    string EnvironmentClass,
    string InternetReachability,
    string ConfidentialityRequirement,
    string IntegrityRequirement,
    string AvailabilityRequirement,
    string ModifiedAttackVector,
    string ModifiedAttackComplexity,
    string ModifiedPrivilegesRequired,
    string ModifiedUserInteraction,
    string ModifiedScope,
    string ModifiedConfidentialityImpact,
    string ModifiedIntegrityImpact,
    string ModifiedAvailabilityImpact
);

public record AssignAssetSecurityProfileRequest(Guid? SecurityProfileId);
