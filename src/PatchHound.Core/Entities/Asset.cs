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

    public void UpdateMetadata(string metadata)
    {
        Metadata = metadata;
    }
}
