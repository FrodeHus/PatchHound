using System.Text.Json;

namespace PatchHound.Infrastructure.AuthenticatedScans;

public record ValidatedSoftwareEntry(
    string Name,
    string? Vendor,
    string? Version,
    string? InstallPath);

public record ValidationIssueRecord(int EntryIndex, string FieldPath, string Message);

public record OutputValidationResult(
    bool FatalError,
    string FatalErrorMessage,
    List<ValidatedSoftwareEntry> ValidEntries,
    List<ValidationIssueRecord> Issues);

public class AuthenticatedScanOutputValidator
{
    private const int MaxEntries = 5000;
    private const int MaxStringLength = 1024;

    public OutputValidationResult Validate(string json)
    {
        var valid = new List<ValidatedSoftwareEntry>();
        var issues = new List<ValidationIssueRecord>();

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { return Fatal($"invalid JSON: {ex.Message}"); }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return Fatal("root must be an object");
            if (!doc.RootElement.TryGetProperty("software", out var arr))
                return Fatal("missing 'software' property");
            if (arr.ValueKind != JsonValueKind.Array)
                return Fatal("'software' must be an array");
            var count = arr.GetArrayLength();
            if (count > MaxEntries)
                return Fatal($"entry count {count} exceeds limit of {MaxEntries}");

            var index = 0;
            foreach (var element in arr.EnumerateArray())
            {
                var entryValid = TryValidateEntry(element, index, issues, out var entry);
                if (entryValid) valid.Add(entry!);
                index++;
            }
        }

        return new OutputValidationResult(false, string.Empty, valid, issues);
    }

    private static OutputValidationResult Fatal(string msg) =>
        new(true, msg, new(), new());

    private static bool TryValidateEntry(JsonElement el, int index, List<ValidationIssueRecord> issues, out ValidatedSoftwareEntry? entry)
    {
        entry = null;
        if (el.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new(index, "$", "entry must be an object"));
            return false;
        }

        var name = ReadRequiredString(el, "name", index, issues);
        if (name is null) return false;
        var vendor = ReadOptionalString(el, "vendor", index, issues);
        var version = ReadOptionalString(el, "version", index, issues);
        var installPath = ReadOptionalString(el, "installPath", index, issues);
        entry = new ValidatedSoftwareEntry(name, vendor, version, installPath);
        return true;
    }

    private static string? ReadRequiredString(JsonElement el, string prop, int index, List<ValidationIssueRecord> issues)
    {
        if (!el.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            issues.Add(new(index, $"$.software[{index}].{prop}", $"missing required field '{prop}'"));
            return null;
        }
        if (v.ValueKind != JsonValueKind.String)
        {
            issues.Add(new(index, $"$.software[{index}].{prop}", $"'{prop}' must be a string"));
            return null;
        }
        var s = v.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(s))
        {
            issues.Add(new(index, $"$.software[{index}].{prop}", $"'{prop}' cannot be empty"));
            return null;
        }
        if (s.Length > MaxStringLength)
        {
            issues.Add(new(index, $"$.software[{index}].{prop}", $"'{prop}' exceeds {MaxStringLength} chars"));
            return null;
        }
        return s;
    }

    private static string? ReadOptionalString(JsonElement el, string prop, int index, List<ValidationIssueRecord> issues)
    {
        if (!el.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.String)
        {
            issues.Add(new(index, $"$.software[{index}].{prop}", $"'{prop}' must be a string"));
            return null;
        }
        var s = v.GetString() ?? "";
        if (s.Length > MaxStringLength)
        {
            issues.Add(new(index, $"$.software[{index}].{prop}", $"'{prop}' exceeds {MaxStringLength} chars"));
            return null;
        }
        return s;
    }
}
