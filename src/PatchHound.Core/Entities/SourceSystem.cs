namespace PatchHound.Core.Entities;

public class SourceSystem
{
    public Guid Id { get; private set; }
    public string Key { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    private SourceSystem() { }

    public static SourceSystem Create(string key, string displayName)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key is required.", nameof(key));
        }
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("DisplayName is required.", nameof(displayName));
        }

        var normalizedKey = key.Trim().ToLowerInvariant();
        var normalizedDisplayName = displayName.Trim();

        if (normalizedKey.Length > 64)
        {
            throw new ArgumentException("Key must be 64 characters or fewer.", nameof(key));
        }
        if (normalizedDisplayName.Length > 256)
        {
            throw new ArgumentException("DisplayName must be 256 characters or fewer.", nameof(displayName));
        }

        return new SourceSystem
        {
            Id = Guid.NewGuid(),
            Key = normalizedKey,
            DisplayName = normalizedDisplayName,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
