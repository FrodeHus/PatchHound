using System.Text.Json;
using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class AdvancedTool
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string SupportedAssetTypesJson { get; private set; } = "[]";
    public string KqlQuery { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private AdvancedTool() { }

    public static AdvancedTool Create(
        string name,
        string description,
        IReadOnlyList<AdvancedToolAssetType> supportedAssetTypes,
        string kqlQuery,
        bool enabled
    )
    {
        var now = DateTimeOffset.UtcNow;
        return new AdvancedTool
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description.Trim(),
            SupportedAssetTypesJson = JsonSerializer.Serialize(supportedAssetTypes, JsonOptions),
            KqlQuery = kqlQuery.Trim(),
            Enabled = enabled,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(
        string name,
        string description,
        IReadOnlyList<AdvancedToolAssetType> supportedAssetTypes,
        string kqlQuery,
        bool enabled
    )
    {
        Name = name.Trim();
        Description = description.Trim();
        SupportedAssetTypesJson = JsonSerializer.Serialize(supportedAssetTypes, JsonOptions);
        KqlQuery = kqlQuery.Trim();
        Enabled = enabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public IReadOnlyList<AdvancedToolAssetType> GetSupportedAssetTypes()
    {
        return JsonSerializer.Deserialize<List<AdvancedToolAssetType>>(
                SupportedAssetTypesJson,
                JsonOptions
            ) ?? [];
    }
}
