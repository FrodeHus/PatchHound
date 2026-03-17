namespace PatchHound.Core.Entities;

public class AssetTag
{
    public Guid Id { get; private set; }
    public Guid AssetId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Tag { get; private set; } = null!;
    public string Source { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    private AssetTag() { }

    public static AssetTag Create(Guid tenantId, Guid assetId, string tag, string source)
    {
        return new AssetTag
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AssetId = assetId,
            Tag = tag,
            Source = source,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
