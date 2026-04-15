using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class StagedDevice
{
    public Guid Id { get; private set; }
    public Guid IngestionRunId { get; private set; }
    public int BatchNumber { get; private set; }
    public Guid TenantId { get; private set; }
    public string SourceKey { get; private set; } = string.Empty;
    public string ExternalId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public AssetType AssetType { get; private set; }
    public string PayloadJson { get; private set; } = string.Empty;
    public DateTimeOffset StagedAt { get; private set; }

    private StagedDevice() { }

    public static StagedDevice Create(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        string externalId,
        string name,
        AssetType assetType,
        string payloadJson,
        DateTimeOffset stagedAt,
        int batchNumber = 0
    )
    {
        return new StagedDevice
        {
            Id = Guid.NewGuid(),
            IngestionRunId = ingestionRunId,
            BatchNumber = batchNumber,
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
