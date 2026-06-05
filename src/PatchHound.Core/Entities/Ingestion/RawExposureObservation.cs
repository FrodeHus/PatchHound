namespace PatchHound.Core.Entities.Ingestion;

public class RawExposureObservation
{
    public Guid Id { get; private set; }
    public Guid IngestionRunId { get; private set; }
    public int BatchNumber { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SourceSystemId { get; private set; }
    public string DeviceExternalId { get; private set; } = string.Empty;
    public string VulnerabilityExternalId { get; private set; } = string.Empty;
    public string SoftwareExternalId { get; private set; } = string.Empty;
    public string? SoftwareVersion { get; private set; }
    public DateTimeOffset ObservedAt { get; private set; }

    private RawExposureObservation() { }

    public static RawExposureObservation Create(
        Guid ingestionRunId,
        Guid tenantId,
        Guid sourceSystemId,
        string deviceExternalId,
        string vulnerabilityExternalId,
        string? softwareExternalId,
        string? softwareVersion,
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

        return new RawExposureObservation
        {
            Id = Guid.NewGuid(),
            IngestionRunId = ingestionRunId,
            TenantId = tenantId,
            SourceSystemId = sourceSystemId,
            DeviceExternalId = IngestionEntityValidation.RequireString(deviceExternalId, nameof(deviceExternalId), 256),
            VulnerabilityExternalId = IngestionEntityValidation.RequireString(vulnerabilityExternalId, nameof(vulnerabilityExternalId), 128),
            SoftwareExternalId = IngestionEntityValidation.OptionalString(softwareExternalId, nameof(softwareExternalId), 256) ?? string.Empty,
            SoftwareVersion = IngestionEntityValidation.OptionalString(softwareVersion, nameof(softwareVersion), 128),
            ObservedAt = observedAt,
            BatchNumber = batchNumber,
        };
    }
}
