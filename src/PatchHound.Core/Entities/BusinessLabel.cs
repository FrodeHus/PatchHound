namespace PatchHound.Core.Entities;

public class BusinessLabel
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? Color { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private BusinessLabel() { }

    public static BusinessLabel Create(
        Guid tenantId,
        string name,
        string? description,
        string? color
    )
    {
        var now = DateTimeOffset.UtcNow;
        return new BusinessLabel
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Description = description?.Trim(),
            Color = color?.Trim(),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string name, string? description, string? color, bool isActive)
    {
        Name = name.Trim();
        Description = description?.Trim();
        Color = color?.Trim();
        IsActive = isActive;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
