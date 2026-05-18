namespace PatchHound.Core.Entities.Ingestion;

public class SoftwareSourceIdentity
{
    public Guid Id { get; private set; }
    public Guid SourceSystemId { get; private set; }
    public string ExternalId { get; private set; } = string.Empty;
    public string ObservedVendor { get; private set; } = string.Empty;
    public string ObservedName { get; private set; } = string.Empty;
    public string? ObservedVersion { get; private set; }
    public string CanonicalProductKey { get; private set; } = string.Empty;
    public Guid? SoftwareProductId { get; private set; }
    public Guid? SoftwareReleaseId { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }

    private SoftwareSourceIdentity() { }

    public static SoftwareSourceIdentity Create(
        Guid sourceSystemId,
        string externalId,
        string observedVendor,
        string observedName,
        string? observedVersion,
        DateTimeOffset observedAt)
    {
        IngestionEntityValidation.RequireGuid(sourceSystemId, nameof(sourceSystemId));
        if (observedAt == default)
        {
            throw new ArgumentException("ObservedAt is required.", nameof(observedAt));
        }

        var vendor = IngestionEntityValidation.RequireString(observedVendor, nameof(observedVendor), 256);
        var name = IngestionEntityValidation.RequireString(observedName, nameof(observedName), 512);
        var canonicalKey = $"{vendor.ToLowerInvariant()}::{name.ToLowerInvariant()}";
        if (canonicalKey.Length > 512)
        {
            throw new ArgumentException("CanonicalProductKey must be 512 characters or fewer.", nameof(observedName));
        }

        return new SoftwareSourceIdentity
        {
            Id = Guid.NewGuid(),
            SourceSystemId = sourceSystemId,
            ExternalId = IngestionEntityValidation.RequireString(externalId, nameof(externalId), 256),
            ObservedVendor = vendor,
            ObservedName = name,
            ObservedVersion = IngestionEntityValidation.OptionalString(observedVersion, nameof(observedVersion), 128),
            CanonicalProductKey = canonicalKey,
            FirstSeenAt = observedAt,
            LastSeenAt = observedAt,
        };
    }
}
