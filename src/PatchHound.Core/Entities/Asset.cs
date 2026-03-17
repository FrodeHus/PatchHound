using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class Asset
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string ExternalId { get; private set; } = null!;
    public AssetType AssetType { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public Criticality Criticality { get; private set; }
    public OwnerType OwnerType { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public Guid? OwnerTeamId { get; private set; }
    public Guid? FallbackTeamId { get; private set; }
    public Guid? SecurityProfileId { get; private set; }
    public string? DeviceComputerDnsName { get; private set; }
    public string? DeviceHealthStatus { get; private set; }
    public string? DeviceOsPlatform { get; private set; }
    public string? DeviceOsVersion { get; private set; }
    public string? DeviceRiskScore { get; private set; }
    public DateTimeOffset? DeviceLastSeenAt { get; private set; }
    public string? DeviceLastIpAddress { get; private set; }
    public string? DeviceAadDeviceId { get; private set; }
    public string? DeviceGroupId { get; private set; }
    public string? DeviceGroupName { get; private set; }
    public string? DeviceExposureLevel { get; private set; }
    public bool? DeviceIsAadJoined { get; private set; }
    public string Metadata { get; private set; } = "{}";

    private Asset() { }

    public static Asset Create(
        Guid tenantId,
        string externalId,
        AssetType assetType,
        string name,
        Criticality criticality,
        string? description = null
    )
    {
        return new Asset
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ExternalId = externalId,
            AssetType = assetType,
            Name = name,
            Criticality = criticality,
            Description = description,
            OwnerType = OwnerType.User,
        };
    }

    public void AssignOwner(Guid userId)
    {
        OwnerType = OwnerType.User;
        OwnerUserId = userId;
        OwnerTeamId = null;
    }

    public void AssignTeamOwner(Guid teamId)
    {
        OwnerType = OwnerType.Team;
        OwnerTeamId = teamId;
        OwnerUserId = null;
    }

    public void SetCriticality(Criticality criticality)
    {
        Criticality = criticality;
    }

    public void AssignSecurityProfile(Guid? securityProfileId)
    {
        SecurityProfileId = securityProfileId;
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

    public void UpdateDeviceDetails(
        string? computerDnsName,
        string? healthStatus,
        string? osPlatform,
        string? osVersion,
        string? riskScore,
        DateTimeOffset? lastSeenAt,
        string? lastIpAddress,
        string? aadDeviceId,
        string? groupId = null,
        string? groupName = null,
        string? exposureLevel = null,
        bool? isAadJoined = null
    )
    {
        DeviceComputerDnsName = computerDnsName;
        DeviceHealthStatus = healthStatus;
        DeviceOsPlatform = osPlatform;
        DeviceOsVersion = osVersion;
        DeviceRiskScore = riskScore;
        DeviceLastSeenAt = lastSeenAt;
        DeviceLastIpAddress = lastIpAddress;
        DeviceAadDeviceId = aadDeviceId;
        DeviceGroupId = groupId;
        DeviceGroupName = groupName;
        DeviceExposureLevel = exposureLevel;
        DeviceIsAadJoined = isAadJoined;
    }
}
