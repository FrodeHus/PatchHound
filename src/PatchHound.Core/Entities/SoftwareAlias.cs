namespace PatchHound.Core.Entities;

public class SoftwareAlias
{
    public Guid Id { get; private set; }
    public Guid SoftwareProductId { get; private set; }
    public Guid SourceSystemId { get; private set; }
    public string ExternalId { get; private set; } = null!;
    public string? ObservedVendor { get; private set; }
    public string? ObservedName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private SoftwareAlias() { }

    public static SoftwareAlias Create(
        Guid softwareProductId,
        Guid sourceSystemId,
        string externalId,
        string? observedVendor = null,
        string? observedName = null)
    {
        if (softwareProductId == Guid.Empty)
        {
            throw new ArgumentException("SoftwareProductId is required.", nameof(softwareProductId));
        }
        if (sourceSystemId == Guid.Empty)
        {
            throw new ArgumentException("SourceSystemId is required.", nameof(sourceSystemId));
        }
        if (string.IsNullOrWhiteSpace(externalId))
        {
            throw new ArgumentException("ExternalId is required.", nameof(externalId));
        }

        var normalizedExternalId = externalId.Trim();
        if (normalizedExternalId.Length > 256)
        {
            throw new ArgumentException("ExternalId must be 256 characters or fewer.", nameof(externalId));
        }

        if (observedVendor is not null && observedVendor.Length > 256)
        {
            throw new ArgumentException("ObservedVendor must be 256 characters or fewer.", nameof(observedVendor));
        }

        if (observedName is not null && observedName.Length > 512)
        {
            throw new ArgumentException("ObservedName must be 512 characters or fewer.", nameof(observedName));
        }

        return new SoftwareAlias
        {
            Id = Guid.NewGuid(),
            SoftwareProductId = softwareProductId,
            SourceSystemId = sourceSystemId,
            ExternalId = normalizedExternalId,
            ObservedVendor = observedVendor,
            ObservedName = observedName,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
