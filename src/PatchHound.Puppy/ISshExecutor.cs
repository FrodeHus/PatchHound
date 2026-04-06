namespace PatchHound.Puppy;

public interface ISshExecutor
{
    Task<ScriptResult> ExecuteToolsAsync(
        HostTarget host,
        JobCredentials credentials,
        string? hostKeyFingerprint,
        IReadOnlyList<ToolPayload> tools,
        CancellationToken ct);
}

public record ScriptResult(
    bool Success,
    string CombinedStdout,
    string CombinedStderr,
    string? ErrorMessage);
