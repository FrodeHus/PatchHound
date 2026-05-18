namespace PatchHound.Core.Entities.Ingestion;

public class RawDeviceObservation
{
    public Guid Id { get; private set; }
    public Guid IngestionRunId { get; private set; }
    public int BatchNumber { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SourceSystemId { get; private set; }
    public string ExternalId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? ComputerDnsName { get; private set; }
    public string? HealthStatus { get; private set; }
    public string? OsPlatform { get; private set; }
    public string? OsVersion { get; private set; }
    public DateTimeOffset? SourceLastSeenAt { get; private set; }
    public DateTimeOffset ObservedAt { get; private set; }

    private RawDeviceObservation() { }

    public static RawDeviceObservation Create(
        Guid ingestionRunId,
        Guid tenantId,
        Guid sourceSystemId,
        string externalId,
        string name,
        DateTimeOffset observedAt,
        int batchNumber = 0,
        string? computerDnsName = null,
        string? healthStatus = null,
        string? osPlatform = null,
        string? osVersion = null,
        DateTimeOffset? sourceLastSeenAt = null)
    {
        IngestionEntityValidation.RequireGuid(ingestionRunId, nameof(ingestionRunId));
        IngestionEntityValidation.RequireGuid(tenantId, nameof(tenantId));
        IngestionEntityValidation.RequireGuid(sourceSystemId, nameof(sourceSystemId));
        if (observedAt == default)
        {
            throw new ArgumentException("ObservedAt is required.", nameof(observedAt));
        }

        return new RawDeviceObservation
        {
            Id = Guid.NewGuid(),
            IngestionRunId = ingestionRunId,
            TenantId = tenantId,
            SourceSystemId = sourceSystemId,
            ExternalId = IngestionEntityValidation.RequireString(externalId, nameof(externalId), 256),
            Name = IngestionEntityValidation.RequireString(name, nameof(name), 512),
            ObservedAt = observedAt,
            BatchNumber = batchNumber,
            ComputerDnsName = IngestionEntityValidation.OptionalString(computerDnsName, nameof(computerDnsName), 256),
            HealthStatus = IngestionEntityValidation.OptionalString(healthStatus, nameof(healthStatus), 64),
            OsPlatform = IngestionEntityValidation.OptionalString(osPlatform, nameof(osPlatform), 128),
            OsVersion = IngestionEntityValidation.OptionalString(osVersion, nameof(osVersion), 128),
            SourceLastSeenAt = sourceLastSeenAt,
        };
    }
}
