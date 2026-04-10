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
        return new SourceSystem
        {
            Id = Guid.NewGuid(),
            Key = key.Trim().ToLowerInvariant(),
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
