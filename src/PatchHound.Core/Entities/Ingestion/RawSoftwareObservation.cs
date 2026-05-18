namespace PatchHound.Core.Entities.Ingestion;

public class RawSoftwareObservation
{
    public Guid Id { get; private set; }
    public Guid IngestionRunId { get; private set; }
    public int BatchNumber { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SourceSystemId { get; private set; }
    public string ExternalId { get; private set; } = string.Empty;
    public string Vendor { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Version { get; private set; }
    public string CanonicalProductKey { get; private set; } = string.Empty;
    public DateTimeOffset ObservedAt { get; private set; }

    private RawSoftwareObservation() { }

    public static RawSoftwareObservation Create(
        Guid ingestionRunId,
        Guid tenantId,
        Guid sourceSystemId,
        string externalId,
        string vendor,
        string name,
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

        var normalizedVendor = IngestionEntityValidation.RequireString(vendor, nameof(vendor), 256);
        var normalizedName = IngestionEntityValidation.RequireString(name, nameof(name), 512);
        var canonicalKey = $"{normalizedVendor.ToLowerInvariant()}::{normalizedName.ToLowerInvariant()}";
        if (canonicalKey.Length > 512)
        {
            throw new ArgumentException("CanonicalProductKey must be 512 characters or fewer.", nameof(name));
        }

        return new RawSoftwareObservation
        {
            Id = Guid.NewGuid(),
            IngestionRunId = ingestionRunId,
            TenantId = tenantId,
            SourceSystemId = sourceSystemId,
            ExternalId = IngestionEntityValidation.RequireString(externalId, nameof(externalId), 256),
            Vendor = normalizedVendor,
            Name = normalizedName,
            Version = IngestionEntityValidation.OptionalString(version, nameof(version), 128),
            CanonicalProductKey = canonicalKey,
            ObservedAt = observedAt,
            BatchNumber = batchNumber,
        };
    }
}
