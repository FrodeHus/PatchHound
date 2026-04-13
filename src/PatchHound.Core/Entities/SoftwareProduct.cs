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
        if (string.IsNullOrWhiteSpace(vendor))
        {
            throw new ArgumentException("Vendor is required.", nameof(vendor));
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        var normalizedVendor = vendor.Trim();
        var normalizedName = name.Trim();

        if (normalizedVendor.Length > 256)
        {
            throw new ArgumentException("Vendor must be 256 characters or fewer.", nameof(vendor));
        }
        if (normalizedName.Length > 512)
        {
            throw new ArgumentException("Name must be 512 characters or fewer.", nameof(name));
        }

        var canonicalProductKey = $"{normalizedVendor.ToLowerInvariant()}::{normalizedName.ToLowerInvariant()}";
        if (canonicalProductKey.Length > 512)
        {
            throw new ArgumentException("Combined vendor and name exceed the 512-character canonical key limit.", nameof(name));
        }

        if (primaryCpe23Uri is not null && primaryCpe23Uri.Length > 512)
        {
            throw new ArgumentException("PrimaryCpe23Uri must be 512 characters or fewer.", nameof(primaryCpe23Uri));
        }

        var now = DateTimeOffset.UtcNow;
        return new SoftwareProduct
        {
            Id = Guid.NewGuid(),
            Vendor = normalizedVendor,
            Name = normalizedName,
            CanonicalProductKey = canonicalProductKey,
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
