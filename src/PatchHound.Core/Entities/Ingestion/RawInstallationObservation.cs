namespace PatchHound.Core.Entities.Ingestion;

public class RawInstallationObservation
{
    public Guid Id { get; private set; }
    public Guid IngestionRunId { get; private set; }
    public int BatchNumber { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SourceSystemId { get; private set; }
    public string DeviceExternalId { get; private set; } = string.Empty;
    public string SoftwareExternalId { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public DateTimeOffset ObservedAt { get; private set; }

    private RawInstallationObservation() { }

    public static RawInstallationObservation Create(
        Guid ingestionRunId,
        Guid tenantId,
        Guid sourceSystemId,
        string deviceExternalId,
        string softwareExternalId,
        string? version,
        DateTimeOffset observedAt,
        int batchNumber = 0)
    {
        IngestionEntityValidation.RequireGuid(ingestionRunId, nameof(ingestionRunId));
        IngestionEntityValidation.RequireGuid(tenantId, nameof(tenantId));
        IngestionEntityValidation.RequireGuid(sourceSystemId, nameof(sourceSystemId));
        if (observedAt == default)
        {
            throw new ArgumentException("ObservedAt is required.", nameof(observedAt));
        }

        return new RawInstallationObservation
        {
            Id = Guid.NewGuid(),
            IngestionRunId = ingestionRunId,
            TenantId = tenantId,
            SourceSystemId = sourceSystemId,
            DeviceExternalId = IngestionEntityValidation.RequireString(deviceExternalId, nameof(deviceExternalId), 256),
            SoftwareExternalId = IngestionEntityValidation.RequireString(softwareExternalId, nameof(softwareExternalId), 256),
            Version = IngestionEntityValidation.OptionalString(version, nameof(version), 128) ?? string.Empty,
            ObservedAt = observedAt,
            BatchNumber = batchNumber,
        };
    }
}
