namespace PatchHound.Core.Entities.Ingestion;

internal static class IngestionEntityValidation
{
    public static void RequireGuid(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{name} is required.", name);
        }
    }

    public static string RequireString(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return OptionalString(value, name, maxLength)!;
    }

    public static string? OptionalString(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"{name} must be {maxLength} characters or fewer.", name);
        }

        return normalized;
    }
}
