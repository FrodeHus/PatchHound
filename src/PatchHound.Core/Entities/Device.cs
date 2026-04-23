using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class Device
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SourceSystemId { get; private set; }
    public string ExternalId { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    public Criticality BaselineCriticality { get; private set; }
    public Criticality Criticality { get; private set; }
    public string? CriticalitySource { get; private set; }
    public string? CriticalityReason { get; private set; }
    public Guid? CriticalityRuleId { get; private set; }
    public DateTimeOffset? CriticalityUpdatedAt { get; private set; }

    public OwnerType OwnerType { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public Guid? OwnerTeamId { get; private set; }
    public Guid? OwnerTeamRuleId { get; private set; }
    public Guid? FallbackTeamId { get; private set; }
    public Guid? FallbackTeamRuleId { get; private set; }

    public Guid? SecurityProfileId { get; private set; }
    public Guid? SecurityProfileRuleId { get; private set; }

    public string? ComputerDnsName { get; private set; }
    public string? HealthStatus { get; private set; }
    public string? OsPlatform { get; private set; }
    public string? OsVersion { get; private set; }
    public string? ExternalRiskLabel { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }
    public string? LastIpAddress { get; private set; }
    public string? AadDeviceId { get; private set; }
    public string? GroupId { get; private set; }
    public string? GroupName { get; private set; }
    public string? ExposureLevel { get; private set; }
    public bool? IsAadJoined { get; private set; }
    public string? OnboardingStatus { get; private set; }
    public string? DeviceValue { get; private set; }
    public decimal? ExposureImpactScore { get; private set; }
    public bool ActiveInTenant { get; private set; } = true;
    public string Metadata { get; private set; } = "{}";

    private Device() { }

    public static Device Create(
        Guid tenantId,
        Guid sourceSystemId,
        string externalId,
        string name,
        Criticality baselineCriticality,
        string? description = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }
        if (sourceSystemId == Guid.Empty)
        {
            throw new ArgumentException("SourceSystemId is required.", nameof(sourceSystemId));
        }
        if (string.IsNullOrWhiteSpace(externalId))
        {
            throw new ArgumentException("ExternalId is required.", nameof(externalId));
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        var normalizedExternalId = externalId.Trim();
        var normalizedName = name.Trim();
        var normalizedDescription = description?.Trim();

        if (normalizedExternalId.Length > 256)
        {
            throw new ArgumentException("ExternalId must be 256 characters or fewer.", nameof(externalId));
        }
        if (normalizedName.Length > 256)
        {
            throw new ArgumentException("Name must be 256 characters or fewer.", nameof(name));
        }
        if (normalizedDescription is not null && normalizedDescription.Length > 2048)
        {
            throw new ArgumentException("Description must be 2048 characters or fewer.", nameof(description));
        }

        return new Device
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceSystemId = sourceSystemId,
            ExternalId = normalizedExternalId,
            Name = normalizedName,
            Description = normalizedDescription,
            BaselineCriticality = baselineCriticality,
            Criticality = baselineCriticality,
            CriticalitySource = "Default",
            CriticalityUpdatedAt = DateTimeOffset.UtcNow,
            OwnerType = OwnerType.User,
            ActiveInTenant = true,
        };
    }

    public void AssignOwner(Guid userId)
    {
        OwnerType = OwnerType.User;
        OwnerUserId = userId;
        OwnerTeamId = null;
        OwnerTeamRuleId = null;
    }

    public void AssignTeamOwner(Guid teamId)
    {
        OwnerType = OwnerType.Team;
        OwnerTeamId = teamId;
        OwnerUserId = null;
        OwnerTeamRuleId = null;
    }

    public void AssignTeamOwnerFromRule(Guid? teamId, Guid ruleId)
    {
        if (teamId.HasValue)
        {
            OwnerType = OwnerType.Team;
            OwnerTeamId = teamId;
            OwnerUserId = null;
            OwnerTeamRuleId = ruleId;
            return;
        }

        if (OwnerTeamRuleId == ruleId)
        {
            OwnerType = OwnerType.User;
            OwnerTeamId = null;
            OwnerUserId = null;
            OwnerTeamRuleId = null;
        }
    }

    public void ClearRuleAssignedOwnerTeam(Guid ruleId)
    {
        if (OwnerTeamRuleId != ruleId)
        {
            return;
        }

        OwnerType = OwnerType.User;
        OwnerTeamId = null;
        OwnerUserId = null;
        OwnerTeamRuleId = null;
    }

    public void SetFallbackTeamFromRule(Guid? teamId, Guid ruleId)
    {
        FallbackTeamId = teamId;
        FallbackTeamRuleId = teamId.HasValue ? ruleId : null;
    }

    public void ClearRuleAssignedFallbackTeam(Guid ruleId)
    {
        if (FallbackTeamRuleId != ruleId)
        {
            return;
        }

        FallbackTeamId = null;
        FallbackTeamRuleId = null;
    }

    public void SetCriticality(Criticality criticality)
    {
        Criticality = criticality;
        CriticalitySource = "ManualOverride";
        CriticalityReason = "Set manually.";
        CriticalityRuleId = null;
        CriticalityUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCriticalityFromRule(
        Criticality criticality,
        Guid ruleId,
        string? reason
    )
    {
        Criticality = criticality;
        CriticalitySource = "Rule";
        CriticalityReason = reason;
        CriticalityRuleId = ruleId;
        CriticalityUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ResetCriticalityToBaseline()
    {
        Criticality = BaselineCriticality;
        CriticalitySource = "Default";
        CriticalityReason = "No criticality rule currently matches this asset.";
        CriticalityRuleId = null;
        CriticalityUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearManualCriticalityOverride()
    {
        Criticality = BaselineCriticality;
        CriticalitySource = "Default";
        CriticalityReason = "Manual override removed.";
        CriticalityRuleId = null;
        CriticalityUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AssignSecurityProfile(Guid? securityProfileId)
    {
        SecurityProfileId = securityProfileId;
        SecurityProfileRuleId = null;
    }

    public void AssignSecurityProfileFromRule(Guid? securityProfileId, Guid ruleId)
    {
        SecurityProfileId = securityProfileId;
        SecurityProfileRuleId = securityProfileId.HasValue ? ruleId : null;
    }

    public void ClearRuleAssignedSecurityProfile(Guid ruleId)
    {
        if (SecurityProfileRuleId != ruleId)
        {
            return;
        }

        SecurityProfileId = null;
        SecurityProfileRuleId = null;
    }

    public void UpdateDetails(string name, string? description = null)
    {
        Name = name;
        Description = description;
    }

    public void UpdateMetadata(string metadata)
    {
        Metadata = metadata;
    }

    public void UpdateInventoryDetails(
        string? computerDnsName,
        string? healthStatus,
        string? osPlatform,
        string? osVersion,
        string? externalRiskLabel,
        DateTimeOffset? lastSeenAt,
        string? lastIpAddress,
        string? aadDeviceId,
        string? groupId = null,
        string? groupName = null,
        string? exposureLevel = null,
        bool? isAadJoined = null,
        string? onboardingStatus = null,
        string? deviceValue = null
    )
    {
        ComputerDnsName = computerDnsName;
        HealthStatus = healthStatus;
        OsPlatform = osPlatform;
        OsVersion = osVersion;
        ExternalRiskLabel = externalRiskLabel;
        LastSeenAt = lastSeenAt;
        LastIpAddress = lastIpAddress;
        AadDeviceId = aadDeviceId;
        GroupId = groupId;
        GroupName = groupName;
        ExposureLevel = exposureLevel;
        IsAadJoined = isAadJoined;
        OnboardingStatus = onboardingStatus;
        DeviceValue = deviceValue;
    }

    public void SetActiveInTenant(bool isActive)
    {
        ActiveInTenant = isActive;
    }

    public void SetExposureImpactScore(decimal? score)
    {
        ExposureImpactScore = score.HasValue ? Math.Clamp(Math.Round(score.Value, 1), 0m, 100m) : null;
    }
}
