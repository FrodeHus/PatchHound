using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class BusinessLabel
{
    public static readonly IReadOnlyDictionary<BusinessLabelWeightCategory, decimal> CategoryWeights =
        new Dictionary<BusinessLabelWeightCategory, decimal>
        {
            [BusinessLabelWeightCategory.Informational] = 0.5m,
            [BusinessLabelWeightCategory.Normal] = 1.0m,
            [BusinessLabelWeightCategory.Sensitive] = 1.5m,
            [BusinessLabelWeightCategory.Critical] = 2.0m,
        };

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? Color { get; private set; }
    public bool IsActive { get; private set; }
    public BusinessLabelWeightCategory WeightCategory { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public decimal RiskWeight => CategoryWeights[WeightCategory];

    private BusinessLabel() { }

    public static BusinessLabel Create(
        Guid tenantId,
        string name,
        string? description,
        string? color,
        BusinessLabelWeightCategory weightCategory = BusinessLabelWeightCategory.Normal
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
            WeightCategory = weightCategory,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string name, string? description, string? color, bool isActive, BusinessLabelWeightCategory weightCategory = BusinessLabelWeightCategory.Normal)
    {
        Name = name.Trim();
        Description = description?.Trim();
        Color = color?.Trim();
        IsActive = isActive;
        WeightCategory = weightCategory;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
