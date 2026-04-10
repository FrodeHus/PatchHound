namespace PatchHound.Core.Entities;

public class SoftwareProduct
{
    public Guid Id { get; private set; }
    public string CanonicalProductKey { get; private set; } = null!;
    public string Vendor { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? PrimaryCpe23Uri { get; private set; }
    public DateTimeOffset? EndOfLifeAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private SoftwareProduct() { }

    public static SoftwareProduct Create(string vendor, string name, string? primaryCpe23Uri)
    {
        if (string.IsNullOrWhiteSpace(vendor)) throw new ArgumentException("Vendor is required.", nameof(vendor));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        var now = DateTimeOffset.UtcNow;
        return new SoftwareProduct
        {
            Id = Guid.NewGuid(),
            Vendor = vendor.Trim(),
            Name = name.Trim(),
            CanonicalProductKey = $"{vendor.Trim().ToLowerInvariant()}::{name.Trim().ToLowerInvariant()}",
            PrimaryCpe23Uri = primaryCpe23Uri,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void UpdatePrimaryCpe(string? cpe)
    {
        PrimaryCpe23Uri = cpe;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetEndOfLife(DateTimeOffset? at)
    {
        EndOfLifeAt = at;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
