namespace PatchHound.Core.Entities;

public class StagedDeviceSoftwareInstallation
{
    public Guid Id { get; private set; }
    public Guid IngestionRunId { get; private set; }
    public int BatchNumber { get; private set; }
    public Guid TenantId { get; private set; }
    public string SourceKey { get; private set; } = string.Empty;
    public string DeviceExternalId { get; private set; } = string.Empty;
    public string SoftwareExternalId { get; private set; } = string.Empty;
    public DateTimeOffset ObservedAt { get; private set; }
    public string PayloadJson { get; private set; } = string.Empty;
    public DateTimeOffset StagedAt { get; private set; }

    private StagedDeviceSoftwareInstallation() { }

    public static StagedDeviceSoftwareInstallation Create(
        Guid ingestionRunId,
        Guid tenantId,
        string sourceKey,
        string deviceExternalId,
        string softwareExternalId,
        DateTimeOffset observedAt,
        string payloadJson,
        DateTimeOffset stagedAt,
        int batchNumber = 0
    )
    {
        return new StagedDeviceSoftwareInstallation
        {
            Id = Guid.NewGuid(),
            IngestionRunId = ingestionRunId,
            BatchNumber = batchNumber,
            TenantId = tenantId,
            SourceKey = sourceKey,
            DeviceExternalId = deviceExternalId,
            SoftwareExternalId = softwareExternalId,
            ObservedAt = observedAt,
            PayloadJson = payloadJson,
            StagedAt = stagedAt,
        };
    }
}
