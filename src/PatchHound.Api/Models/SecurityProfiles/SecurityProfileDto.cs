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
    DateTimeOffset UpdatedAt
);

public record CreateSecurityProfileRequest(
    Guid TenantId,
    string Name,
    string? Description,
    string EnvironmentClass,
    string InternetReachability,
    string ConfidentialityRequirement,
    string IntegrityRequirement,
    string AvailabilityRequirement
);

public record UpdateSecurityProfileRequest(
    string Name,
    string? Description,
    string EnvironmentClass,
    string InternetReachability,
    string ConfidentialityRequirement,
    string IntegrityRequirement,
    string AvailabilityRequirement
);

public record AssignAssetSecurityProfileRequest(Guid? SecurityProfileId);
