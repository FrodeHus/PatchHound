using System.Text.Json;

namespace PatchHound.Infrastructure.Services;

internal static class StagingSerializerOptions
{
    public static readonly JsonSerializerOptions Instance = new(JsonSerializerDefaults.Web);
}
