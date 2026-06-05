namespace PatchHound.Core.Entities.Ingestion;

public class SoftwareRelease
{
    public Guid Id { get; private set; }
    public Guid SoftwareProductId { get; private set; }
    public string NormalizedVersion { get; private set; } = string.Empty;
    public string RawVersion { get; private set; } = string.Empty;
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }

    private SoftwareRelease() { }

    public static SoftwareRelease Create(
        Guid softwareProductId,
        string? rawVersion,
        DateTimeOffset observedAt)
    {
        IngestionEntityValidation.RequireGuid(softwareProductId, nameof(softwareProductId));
        if (observedAt == default)
        {
            throw new ArgumentException("ObservedAt is required.", nameof(observedAt));
        }

        var normalized = IngestionEntityValidation.OptionalString(rawVersion, nameof(rawVersion), 128) ?? string.Empty;
        return new SoftwareRelease
        {
            Id = Guid.NewGuid(),
            SoftwareProductId = softwareProductId,
            RawVersion = normalized,
            NormalizedVersion = normalized.ToLowerInvariant(),
            FirstSeenAt = observedAt,
            LastSeenAt = observedAt,
        };
    }
}
