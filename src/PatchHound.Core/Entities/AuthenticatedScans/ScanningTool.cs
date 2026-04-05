namespace PatchHound.Core.Entities.AuthenticatedScans;

public class ScanningTool
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string ScriptType { get; private set; } = "python";
    public string InterpreterPath { get; private set; } = string.Empty;
    public int TimeoutSeconds { get; private set; } = 300;
    public string OutputModel { get; private set; } = "NormalizedSoftware";
    public Guid? CurrentVersionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ScanningTool() { }

    public static ScanningTool Create(
        Guid tenantId,
        string name,
        string description,
        string scriptType,
        string interpreterPath,
        int timeoutSeconds,
        string outputModel)
    {
        ValidateScriptType(scriptType);
        if (outputModel != "NormalizedSoftware")
            throw new ArgumentException("outputModel must be 'NormalizedSoftware' in v1", nameof(outputModel));
        if (timeoutSeconds is < 5 or > 3600)
            throw new ArgumentException("timeoutSeconds must be between 5 and 3600", nameof(timeoutSeconds));
        var now = DateTimeOffset.UtcNow;
        return new ScanningTool
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Description = description.Trim(),
            ScriptType = scriptType,
            InterpreterPath = interpreterPath.Trim(),
            TimeoutSeconds = timeoutSeconds,
            OutputModel = outputModel,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void UpdateMetadata(string name, string description, string scriptType, string interpreterPath, int timeoutSeconds)
    {
        ValidateScriptType(scriptType);
        if (timeoutSeconds is < 5 or > 3600)
            throw new ArgumentException("timeoutSeconds must be between 5 and 3600", nameof(timeoutSeconds));
        Name = name.Trim();
        Description = description.Trim();
        ScriptType = scriptType;
        InterpreterPath = interpreterPath.Trim();
        TimeoutSeconds = timeoutSeconds;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCurrentVersion(Guid versionId)
    {
        CurrentVersionId = versionId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ValidateScriptType(string value)
    {
        if (value is not ("python" or "bash" or "powershell"))
            throw new ArgumentException("scriptType must be python|bash|powershell", nameof(value));
    }
}
