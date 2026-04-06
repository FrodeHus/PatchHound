using System.Text.Json.Serialization;

namespace PatchHound.Puppy;

public record HeartbeatRequest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("hostname")] string Hostname);

public record JobPayload(
    [property: JsonPropertyName("jobId")] Guid JobId,
    [property: JsonPropertyName("assetId")] Guid AssetId,
    [property: JsonPropertyName("hostTarget")] HostTarget HostTarget,
    [property: JsonPropertyName("credentials")] JobCredentials Credentials,
    [property: JsonPropertyName("hostKeyFingerprint")] string? HostKeyFingerprint,
    [property: JsonPropertyName("tools")] List<ToolPayload> Tools,
    [property: JsonPropertyName("leaseExpiresAt")] DateTimeOffset LeaseExpiresAt);

public record HostTarget(
    [property: JsonPropertyName("host")] string Host,
    [property: JsonPropertyName("port")] int Port,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("authMethod")] string AuthMethod);

public record JobCredentials(
    [property: JsonPropertyName("password")] string? Password,
    [property: JsonPropertyName("privateKey")] string? PrivateKey,
    [property: JsonPropertyName("passphrase")] string? Passphrase);

public record ToolPayload(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("scriptType")] string ScriptType,
    [property: JsonPropertyName("interpreterPath")] string InterpreterPath,
    [property: JsonPropertyName("timeoutSeconds")] int TimeoutSeconds,
    [property: JsonPropertyName("scriptContent")] string ScriptContent,
    [property: JsonPropertyName("outputModel")] string OutputModel);

public record PostResultRequest(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("stdout")] string Stdout,
    [property: JsonPropertyName("stderr")] string Stderr,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage);
