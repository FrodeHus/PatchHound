using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class StagedAsset
{
    public Guid Id { get; private set; }
    public Guid IngestionRunId { get; private set; }
    public Guid TenantId { get; private set; }
    public string SourceKey { get; private set; } = string.Empty;
    public string ExternalId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public AssetType AssetType { get; private set; }
    public string PayloadJson { get; private set; } = string.Empty;
    public DateTimeOffset StagedAt { get; private set; }

    private StagedAsset() { }

    public static StagedAsset Create(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        string externalId,
        string name,
        AssetType assetType,
        string payloadJson,
        DateTimeOffset stagedAt
    )
    {
        return new StagedAsset
        {
            Id = Guid.NewGuid(),
            IngestionRunId = ingestionRunId,
            TenantId = tenantId,
            SourceKey = sourceKey,
            ExternalId = externalId,
            Name = name,
            AssetType = assetType,
            PayloadJson = payloadJson,
            StagedAt = stagedAt,
        };
    }
}
