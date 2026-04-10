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
        if (string.IsNullOrWhiteSpace(externalId)) throw new ArgumentException("ExternalId required.", nameof(externalId));
        return new SoftwareAlias
        {
            Id = Guid.NewGuid(),
            SoftwareProductId = softwareProductId,
            SourceSystemId = sourceSystemId,
            ExternalId = externalId,
            ObservedVendor = observedVendor,
            ObservedName = observedName,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
