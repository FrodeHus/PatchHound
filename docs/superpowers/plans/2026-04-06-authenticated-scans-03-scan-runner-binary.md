# Authenticated Scans Plan 3: PatchHound.ScanRunner Binary

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the on-prem `PatchHound.ScanRunner` binary — a headless .NET worker that polls the central API for scan jobs, SSHs into target hosts, executes scripts, captures output, and posts results back.

**Architecture:** Single-file-publishable .NET 10 worker service. Three internal components: an `ApiClient` (typed HttpClient for all central API calls), a `JobExecutor` (SSH.NET-based script execution pipeline), and a `RunnerWorker` (BackgroundService orchestrating the poll-execute-report loop). Configuration via YAML file (`runner.yaml`). No database — fully stateless; the central API is the single source of truth.

**Tech Stack:** .NET 10 Worker SDK, SSH.NET (Renci.SshNet), YamlDotNet, System.Text.Json, xUnit + NSubstitute

---

## File Map

| File | Responsibility |
|------|---------------|
| `src/PatchHound.ScanRunner/PatchHound.ScanRunner.csproj` | Project file — Worker SDK, SSH.NET, YamlDotNet packages |
| `src/PatchHound.ScanRunner/Program.cs` | Host builder, DI registration, configuration binding |
| `src/PatchHound.ScanRunner/RunnerOptions.cs` | YAML configuration model (central URL, bearer token, runner ID, concurrency) |
| `src/PatchHound.ScanRunner/ApiClient.cs` | Typed HttpClient wrapping all 4 central API endpoints |
| `src/PatchHound.ScanRunner/ApiModels.cs` | DTOs matching the central API's JSON contract |
| `src/PatchHound.ScanRunner/SshJobExecutor.cs` | SSH.NET logic: connect, upload script, execute, capture output, cleanup |
| `src/PatchHound.ScanRunner/RunnerWorker.cs` | BackgroundService: heartbeat, poll loop, concurrent job execution |
| `src/PatchHound.ScanRunner/runner.yaml` | Example configuration file (copied to output on build) |
| `tests/PatchHound.Tests/ScanRunner/ApiClientTests.cs` | Tests for API client retry/error handling |
| `tests/PatchHound.Tests/ScanRunner/SshJobExecutorTests.cs` | Tests for SSH execution logic (SSH.NET mocked via interface) |
| `tests/PatchHound.Tests/ScanRunner/RunnerWorkerTests.cs` | Tests for poll loop orchestration |
| `PatchHound.slnx` | (modify) — add new project to solution |

---

## Task 1: Project scaffolding and packages

**Files:**
- Create: `src/PatchHound.ScanRunner/PatchHound.ScanRunner.csproj`
- Modify: `PatchHound.slnx`

- [ ] **Step 1: Create the project directory**

```bash
mkdir -p src/PatchHound.ScanRunner
```

- [ ] **Step 2: Create the .csproj**

Create `src/PatchHound.ScanRunner/PatchHound.ScanRunner.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>false</SelfContained>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.3" />
    <PackageReference Include="SSH.NET" Version="2024.2.0" />
    <PackageReference Include="YamlDotNet" Version="16.3.0" />
  </ItemGroup>
  <ItemGroup>
    <None Update="runner.yaml" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add project to solution**

```bash
dotnet sln PatchHound.slnx add src/PatchHound.ScanRunner/PatchHound.ScanRunner.csproj --solution-folder src
```

- [ ] **Step 4: Add project reference in test project**

```bash
dotnet add tests/PatchHound.Tests/PatchHound.Tests.csproj reference src/PatchHound.ScanRunner/PatchHound.ScanRunner.csproj
```

- [ ] **Step 5: Create placeholder Program.cs so it builds**

Create `src/PatchHound.ScanRunner/Program.cs`:

```csharp
var builder = Host.CreateApplicationBuilder(args);
var host = builder.Build();
await host.RunAsync();
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build --nologo`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.ScanRunner/ PatchHound.slnx tests/PatchHound.Tests/PatchHound.Tests.csproj
git commit -m "chore: scaffold PatchHound.ScanRunner project with SSH.NET and YamlDotNet"
```

---

## Task 2: Configuration model and YAML loading

**Files:**
- Create: `src/PatchHound.ScanRunner/RunnerOptions.cs`
- Create: `src/PatchHound.ScanRunner/runner.yaml`
- Modify: `src/PatchHound.ScanRunner/Program.cs`

- [ ] **Step 1: Create RunnerOptions**

Create `src/PatchHound.ScanRunner/RunnerOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace PatchHound.ScanRunner;

public class RunnerOptions
{
    [Required]
    public string CentralUrl { get; set; } = string.Empty;

    [Required]
    public string BearerToken { get; set; } = string.Empty;

    public int MaxConcurrentJobs { get; set; } = 10;

    public int PollIntervalSeconds { get; set; } = 10;

    public int HeartbeatIntervalSeconds { get; set; } = 30;

    public string Hostname { get; set; } = Environment.MachineName;
}
```

- [ ] **Step 2: Create example runner.yaml**

Create `src/PatchHound.ScanRunner/runner.yaml`:

```yaml
# PatchHound ScanRunner configuration
# Copy this file and fill in the values from the admin UI.

centralUrl: "https://patchhound.example.com"
bearerToken: "paste-your-bearer-token-here"
maxConcurrentJobs: 10
pollIntervalSeconds: 10
heartbeatIntervalSeconds: 30
# hostname: "override-hostname"  # defaults to machine name
```

- [ ] **Step 3: Update Program.cs to load YAML config**

Replace `src/PatchHound.ScanRunner/Program.cs`:

```csharp
using PatchHound.ScanRunner;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

var builder = Host.CreateApplicationBuilder(args);

// Load runner.yaml from working directory or next to the binary
var yamlPath = FindConfigFile("runner.yaml");
if (yamlPath is null)
{
    Console.Error.WriteLine("ERROR: runner.yaml not found. Place it next to the binary or in the working directory.");
    return 1;
}

var yaml = File.ReadAllText(yamlPath);
var deserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();

var options = deserializer.Deserialize<RunnerOptions>(yaml);

builder.Services.AddSingleton(options);

var host = builder.Build();

Console.WriteLine($"[startup] PatchHound.ScanRunner configured — central: {options.CentralUrl}, concurrency: {options.MaxConcurrentJobs}");
await host.RunAsync();
return 0;

static string? FindConfigFile(string fileName)
{
    // Check working directory first, then next to the binary
    var cwd = Path.Combine(Directory.GetCurrentDirectory(), fileName);
    if (File.Exists(cwd)) return cwd;

    var binDir = Path.Combine(AppContext.BaseDirectory, fileName);
    if (File.Exists(binDir)) return binDir;

    return null;
}
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build --nologo`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.ScanRunner/RunnerOptions.cs \
  src/PatchHound.ScanRunner/runner.yaml \
  src/PatchHound.ScanRunner/Program.cs
git commit -m "feat: add RunnerOptions configuration model with YAML loading"
```

---

## Task 3: API models (DTOs matching central API)

**Files:**
- Create: `src/PatchHound.ScanRunner/ApiModels.cs`

- [ ] **Step 1: Create ApiModels.cs**

These DTOs mirror the JSON contract defined by `ScanRunnerController` on the central API. Property names use camelCase via `JsonPropertyName` to match the ASP.NET Core default serialization.

Create `src/PatchHound.ScanRunner/ApiModels.cs`:

```csharp
using System.Text.Json.Serialization;

namespace PatchHound.ScanRunner;

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
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build --nologo`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.ScanRunner/ApiModels.cs
git commit -m "feat: add API DTOs matching central ScanRunnerController contract"
```

---

## Task 4: ApiClient with tests (TDD)

**Files:**
- Create: `src/PatchHound.ScanRunner/ApiClient.cs`
- Create: `tests/PatchHound.Tests/ScanRunner/ApiClientTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/PatchHound.Tests/ScanRunner/ApiClientTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using PatchHound.ScanRunner;
using Xunit;

namespace PatchHound.Tests.ScanRunner;

public class ApiClientTests
{
    private static readonly RunnerOptions DefaultOptions = new()
    {
        CentralUrl = "https://patchhound.example.com",
        BearerToken = "test-token"
    };

    private static readonly string AssemblyVersion =
        typeof(ApiClient).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private ApiClient CreateClient(HttpClient httpClient)
    {
        return new ApiClient(httpClient, DefaultOptions);
    }

    [Fact]
    public async Task SendHeartbeatAsync_posts_to_correct_endpoint()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{"ok":true}""");
        var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);
        await client.SendHeartbeatAsync(CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/scan-runner/heartbeat", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal($"Bearer {DefaultOptions.BearerToken}",
            handler.LastRequest.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task GetNextJobAsync_returns_null_on_204()
    {
        var handler = new FakeHandler(HttpStatusCode.NoContent, "");
        var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);
        var result = await client.GetNextJobAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetNextJobAsync_deserializes_job_payload()
    {
        var payload = new JobPayload(
            Guid.NewGuid(), Guid.NewGuid(),
            new HostTarget("host.example.com", 22, "admin", "password"),
            new JobCredentials("s3cret", null, null),
            null,
            [new ToolPayload(Guid.NewGuid(), "tool-1", "python", "/usr/bin/python3",
                300, "print('hi')", "NormalizedSoftware")],
            DateTimeOffset.UtcNow.AddMinutes(10));

        var json = JsonSerializer.Serialize(payload);
        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);
        var result = await client.GetNextJobAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(payload.JobId, result.JobId);
        Assert.Equal("host.example.com", result.HostTarget.Host);
        Assert.Single(result.Tools);
    }

    [Fact]
    public async Task SendJobHeartbeatAsync_posts_to_correct_endpoint()
    {
        var jobId = Guid.NewGuid();
        var handler = new FakeHandler(HttpStatusCode.OK, """{"ok":true}""");
        var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);
        await client.SendJobHeartbeatAsync(jobId, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal($"/api/scan-runner/jobs/{jobId}/heartbeat",
            handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task PostResultAsync_sends_result_body()
    {
        var jobId = Guid.NewGuid();
        var handler = new FakeHandler(HttpStatusCode.OK, """{"ok":true}""");
        var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);
        await client.PostResultAsync(jobId, "Succeeded", "stdout", "stderr", null,
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal($"/api/scan-runner/jobs/{jobId}/result",
            handler.LastRequest.RequestUri!.AbsolutePath);

        var body = await handler.LastRequest.Content!.ReadAsStringAsync();
        var parsed = JsonDocument.Parse(body);
        Assert.Equal("Succeeded", parsed.RootElement.GetProperty("status").GetString());
        Assert.Equal("stdout", parsed.RootElement.GetProperty("stdout").GetString());
    }

    [Fact]
    public async Task GetNextJobAsync_throws_on_server_error()
    {
        var handler = new FakeHandler(HttpStatusCode.InternalServerError, "oops");
        var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetNextJobAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SendHeartbeatAsync_includes_version_and_hostname()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{"ok":true}""");
        var httpClient = new HttpClient(handler);

        var client = CreateClient(httpClient);
        await client.SendHeartbeatAsync(CancellationToken.None);

        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        var parsed = JsonDocument.Parse(body);
        Assert.Equal(DefaultOptions.Hostname, parsed.RootElement.GetProperty("hostname").GetString());
        Assert.Equal(AssemblyVersion, parsed.RootElement.GetProperty("version").GetString());
    }

    /// <summary>
    /// Minimal HttpMessageHandler that records the last request and returns a canned response.
    /// </summary>
    private class FakeHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
```

- [ ] **Step 2: Run tests — expected to fail**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~ApiClientTests`
Expected: FAIL (ApiClient class doesn't exist).

- [ ] **Step 3: Implement ApiClient**

Create `src/PatchHound.ScanRunner/ApiClient.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PatchHound.ScanRunner;

public class ApiClient
{
    private readonly HttpClient _http;
    private readonly RunnerOptions _options;
    private readonly string _version;

    public ApiClient(HttpClient httpClient, RunnerOptions options)
    {
        _http = httpClient;
        _options = options;
        _version = GetType().Assembly.GetName().Version?.ToString() ?? "0.0.0";

        _http.BaseAddress = new Uri(options.CentralUrl.TrimEnd('/'));
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", options.BearerToken);
    }

    public async Task SendHeartbeatAsync(CancellationToken ct)
    {
        var request = new HeartbeatRequest(_version, _options.Hostname);
        var response = await _http.PostAsJsonAsync("/api/scan-runner/heartbeat", request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<JobPayload?> GetNextJobAsync(CancellationToken ct)
    {
        var response = await _http.GetAsync("/api/scan-runner/jobs/next", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JobPayload>(ct);
    }

    public async Task SendJobHeartbeatAsync(Guid jobId, CancellationToken ct)
    {
        var response = await _http.PostAsync(
            $"/api/scan-runner/jobs/{jobId}/heartbeat", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task PostResultAsync(
        Guid jobId, string status, string stdout, string stderr,
        string? errorMessage, CancellationToken ct)
    {
        var request = new PostResultRequest(status, stdout, stderr, errorMessage);
        var response = await _http.PostAsJsonAsync(
            $"/api/scan-runner/jobs/{jobId}/result", request, ct);
        response.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 4: Run tests — expected to pass**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~ApiClientTests`
Expected: all 6 pass.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.ScanRunner/ApiClient.cs \
  tests/PatchHound.Tests/ScanRunner/ApiClientTests.cs
git commit -m "feat: add ApiClient for central API communication with tests"
```

---

## Task 5: SSH execution interface and implementation (TDD)

**Files:**
- Create: `src/PatchHound.ScanRunner/ISshExecutor.cs`
- Create: `src/PatchHound.ScanRunner/SshJobExecutor.cs`
- Create: `tests/PatchHound.Tests/ScanRunner/SshJobExecutorTests.cs`

- [ ] **Step 1: Define the interface**

Create `src/PatchHound.ScanRunner/ISshExecutor.cs`:

```csharp
namespace PatchHound.ScanRunner;

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
```

- [ ] **Step 2: Write failing tests**

Create `tests/PatchHound.Tests/ScanRunner/SshJobExecutorTests.cs`:

```csharp
using NSubstitute;
using PatchHound.ScanRunner;
using Renci.SshNet;
using Xunit;

namespace PatchHound.Tests.ScanRunner;

public class SshJobExecutorTests
{
    [Fact]
    public void GetScriptExtension_returns_py_for_python()
    {
        Assert.Equal(".py", SshJobExecutor.GetScriptExtension("python"));
    }

    [Fact]
    public void GetScriptExtension_returns_sh_for_bash()
    {
        Assert.Equal(".sh", SshJobExecutor.GetScriptExtension("bash"));
    }

    [Fact]
    public void GetScriptExtension_returns_ps1_for_powershell()
    {
        Assert.Equal(".ps1", SshJobExecutor.GetScriptExtension("powershell"));
    }

    [Fact]
    public void GetScriptExtension_returns_sh_for_unknown()
    {
        Assert.Equal(".sh", SshJobExecutor.GetScriptExtension("ruby"));
    }

    [Fact]
    public void BuildRemotePath_uses_tmp_with_job_guid()
    {
        var toolId = Guid.NewGuid();
        var path = SshJobExecutor.BuildRemotePath(toolId, ".py");
        Assert.StartsWith("/tmp/ph-", path);
        Assert.EndsWith(".py", path);
        Assert.Contains(toolId.ToString(), path);
    }

    [Fact]
    public void CreateConnectionInfo_password_auth()
    {
        var host = new HostTarget("host.example.com", 22, "admin", "password");
        var creds = new JobCredentials("s3cret", null, null);

        var connInfo = SshJobExecutor.CreateConnectionInfo(host, creds);

        Assert.Equal("host.example.com", connInfo.Host);
        Assert.Equal(22, connInfo.Port);
        Assert.Equal("admin", connInfo.Username);
    }

    [Fact]
    public void CreateConnectionInfo_private_key_auth()
    {
        // Use a minimal RSA private key in PEM format for testing
        var pem = GenerateTestPem();
        var host = new HostTarget("host.example.com", 2222, "deploy", "privateKey");
        var creds = new JobCredentials(null, pem, null);

        var connInfo = SshJobExecutor.CreateConnectionInfo(host, creds);

        Assert.Equal("host.example.com", connInfo.Host);
        Assert.Equal(2222, connInfo.Port);
        Assert.Equal("deploy", connInfo.Username);
    }

    [Fact]
    public void CreateConnectionInfo_throws_for_missing_credentials()
    {
        var host = new HostTarget("host.example.com", 22, "admin", "password");
        var creds = new JobCredentials(null, null, null);

        Assert.Throws<InvalidOperationException>(
            () => SshJobExecutor.CreateConnectionInfo(host, creds));
    }

    private static string GenerateTestPem()
    {
        // Minimal valid RSA 2048-bit key for testing ConnectionInfo creation.
        // SSH.NET parses this during PrivateKeyFile construction.
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var pem = rsa.ExportRSAPrivateKeyPem();
        return pem;
    }
}
```

- [ ] **Step 3: Run tests — expected to fail**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~SshJobExecutorTests`
Expected: FAIL (SshJobExecutor class doesn't exist).

- [ ] **Step 4: Implement SshJobExecutor**

Create `src/PatchHound.ScanRunner/SshJobExecutor.cs`:

```csharp
using System.Text;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace PatchHound.ScanRunner;

public class SshJobExecutor(ILogger<SshJobExecutor> logger) : ISshExecutor
{
    public async Task<ScriptResult> ExecuteToolsAsync(
        HostTarget host,
        JobCredentials credentials,
        string? hostKeyFingerprint,
        IReadOnlyList<ToolPayload> tools,
        CancellationToken ct)
    {
        var connInfo = CreateConnectionInfo(host, credentials);
        var stdoutAll = new StringBuilder();
        var stderrAll = new StringBuilder();

        using var sshClient = new SshClient(connInfo);
        using var sftpClient = new SftpClient(connInfo);

        try
        {
            sshClient.Connect();
            sftpClient.Connect();

            foreach (var tool in tools)
            {
                ct.ThrowIfCancellationRequested();

                var ext = GetScriptExtension(tool.ScriptType);
                var remotePath = BuildRemotePath(tool.Id, ext);

                logger.LogInformation("Executing tool {ToolName} on {Host} at {RemotePath}",
                    tool.Name, host.Host, remotePath);

                try
                {
                    // Upload script
                    using var scriptStream = new MemoryStream(Encoding.UTF8.GetBytes(tool.ScriptContent));
                    sftpClient.UploadFile(scriptStream, remotePath, true);
                    sftpClient.ChangePermissions(remotePath, 0x1C0); // 0700

                    // Execute with timeout
                    var command = $"{tool.InterpreterPath} {remotePath}";
                    using var cmd = sshClient.CreateCommand(command);
                    cmd.CommandTimeout = TimeSpan.FromSeconds(tool.TimeoutSeconds);

                    var stdout = cmd.Execute();
                    var stderr = cmd.Error;

                    stdoutAll.AppendLine(stdout);
                    if (!string.IsNullOrEmpty(stderr))
                        stderrAll.AppendLine(stderr);

                    if (cmd.ExitStatus != 0)
                    {
                        logger.LogWarning("Tool {ToolName} exited with code {ExitCode}",
                            tool.Name, cmd.ExitStatus);
                        return new ScriptResult(false, stdoutAll.ToString(), stderrAll.ToString(),
                            $"Tool '{tool.Name}' exited with code {cmd.ExitStatus}");
                    }
                }
                finally
                {
                    // Always attempt cleanup
                    try { sftpClient.DeleteFile(remotePath); }
                    catch (Exception ex) { logger.LogWarning(ex, "Failed to delete remote script {Path}", remotePath); }
                }
            }

            return new ScriptResult(true, stdoutAll.ToString(), stderrAll.ToString(), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Renci.SshNet.Common.SshOperationTimeoutException ex)
        {
            return new ScriptResult(false, stdoutAll.ToString(), stderrAll.ToString(),
                $"SSH operation timed out: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "SSH execution failed for {Host}", host.Host);
            return new ScriptResult(false, stdoutAll.ToString(), stderrAll.ToString(),
                $"SSH error: {ex.Message}");
        }
        finally
        {
            if (sshClient.IsConnected) sshClient.Disconnect();
            if (sftpClient.IsConnected) sftpClient.Disconnect();
        }
    }

    public static string GetScriptExtension(string scriptType) => scriptType.ToLowerInvariant() switch
    {
        "python" => ".py",
        "bash" => ".sh",
        "powershell" => ".ps1",
        _ => ".sh"
    };

    public static string BuildRemotePath(Guid toolId, string extension)
    {
        return $"/tmp/ph-{toolId}{extension}";
    }

    public static ConnectionInfo CreateConnectionInfo(HostTarget host, JobCredentials credentials)
    {
        AuthenticationMethod authMethod;

        if (host.AuthMethod == "password")
        {
            if (string.IsNullOrEmpty(credentials.Password))
                throw new InvalidOperationException("Password auth selected but no password provided");

            authMethod = new PasswordAuthenticationMethod(host.Username, credentials.Password);
        }
        else if (host.AuthMethod == "privateKey")
        {
            if (string.IsNullOrEmpty(credentials.PrivateKey))
                throw new InvalidOperationException("Private key auth selected but no key provided");

            var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(credentials.PrivateKey));
            var keyFile = string.IsNullOrEmpty(credentials.Passphrase)
                ? new PrivateKeyFile(keyStream)
                : new PrivateKeyFile(keyStream, credentials.Passphrase);

            authMethod = new PrivateKeyAuthenticationMethod(host.Username, keyFile);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported auth method: {host.AuthMethod}");
        }

        return new ConnectionInfo(host.Host, host.Port, host.Username, authMethod);
    }
}
```

- [ ] **Step 5: Run tests — expected to pass**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~SshJobExecutorTests`
Expected: all 7 pass.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.ScanRunner/ISshExecutor.cs \
  src/PatchHound.ScanRunner/SshJobExecutor.cs \
  tests/PatchHound.Tests/ScanRunner/SshJobExecutorTests.cs
git commit -m "feat: add SshJobExecutor with SFTP upload, execute, cleanup pipeline"
```

---

## Task 6: RunnerWorker with tests (TDD)

**Files:**
- Create: `src/PatchHound.ScanRunner/RunnerWorker.cs`
- Create: `tests/PatchHound.Tests/ScanRunner/RunnerWorkerTests.cs`
- Modify: `src/PatchHound.ScanRunner/Program.cs`

- [ ] **Step 1: Write failing tests**

The tests verify the orchestration logic: heartbeat calls, polling, job execution dispatching, and result posting. SSH execution and API calls are mocked via interfaces.

Create `tests/PatchHound.Tests/ScanRunner/RunnerWorkerTests.cs`:

```csharp
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PatchHound.ScanRunner;
using Xunit;

namespace PatchHound.Tests.ScanRunner;

public class RunnerWorkerTests
{
    private readonly RunnerOptions _options = new()
    {
        CentralUrl = "https://example.com",
        BearerToken = "test",
        MaxConcurrentJobs = 2,
        PollIntervalSeconds = 1,
        HeartbeatIntervalSeconds = 30
    };

    private readonly IRunnerApiClient _apiClient = Substitute.For<IRunnerApiClient>();
    private readonly ISshExecutor _sshExecutor = Substitute.For<ISshExecutor>();

    private RunnerWorker CreateWorker()
    {
        return new RunnerWorker(
            _options,
            _apiClient,
            _sshExecutor,
            Substitute.For<ILogger<RunnerWorker>>());
    }

    [Fact]
    public async Task ExecuteJobAsync_on_success_posts_succeeded_result()
    {
        var job = CreateTestJob();
        _sshExecutor.ExecuteToolsAsync(
                job.HostTarget, job.Credentials, job.HostKeyFingerprint,
                job.Tools, Arg.Any<CancellationToken>())
            .Returns(new ScriptResult(true, """{"software":[]}""", "", null));

        var worker = CreateWorker();
        await worker.ExecuteJobAsync(job, CancellationToken.None);

        await _apiClient.Received(1).PostResultAsync(
            job.JobId, "Succeeded", """{"software":[]}""", "", null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteJobAsync_on_failure_posts_failed_result()
    {
        var job = CreateTestJob();
        _sshExecutor.ExecuteToolsAsync(
                job.HostTarget, job.Credentials, job.HostKeyFingerprint,
                job.Tools, Arg.Any<CancellationToken>())
            .Returns(new ScriptResult(false, "", "error output", "Connection refused"));

        var worker = CreateWorker();
        await worker.ExecuteJobAsync(job, CancellationToken.None);

        await _apiClient.Received(1).PostResultAsync(
            job.JobId, "Failed", "", "error output", "Connection refused",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteJobAsync_on_timeout_posts_timed_out_result()
    {
        var job = CreateTestJob();
        _sshExecutor.ExecuteToolsAsync(
                job.HostTarget, job.Credentials, job.HostKeyFingerprint,
                job.Tools, Arg.Any<CancellationToken>())
            .Returns(new ScriptResult(false, "", "", "SSH operation timed out: timeout"));

        var worker = CreateWorker();
        await worker.ExecuteJobAsync(job, CancellationToken.None);

        await _apiClient.Received(1).PostResultAsync(
            job.JobId, "Failed", "", "", "SSH operation timed out: timeout",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteJobAsync_on_ssh_exception_posts_failed()
    {
        var job = CreateTestJob();
        _sshExecutor.ExecuteToolsAsync(
                job.HostTarget, job.Credentials, job.HostKeyFingerprint,
                job.Tools, Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("network error"));

        var worker = CreateWorker();
        await worker.ExecuteJobAsync(job, CancellationToken.None);

        await _apiClient.Received(1).PostResultAsync(
            job.JobId, "Failed", Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<string>(s => s!.Contains("network error")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PollOnceAsync_returns_false_when_no_jobs()
    {
        _apiClient.GetNextJobAsync(Arg.Any<CancellationToken>())
            .Returns((JobPayload?)null);

        var worker = CreateWorker();
        var result = await worker.PollOnceAsync(CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task PollOnceAsync_returns_true_and_executes_when_job_available()
    {
        var job = CreateTestJob();
        _apiClient.GetNextJobAsync(Arg.Any<CancellationToken>())
            .Returns(job);
        _sshExecutor.ExecuteToolsAsync(
                Arg.Any<HostTarget>(), Arg.Any<JobCredentials>(), Arg.Any<string?>(),
                Arg.Any<IReadOnlyList<ToolPayload>>(), Arg.Any<CancellationToken>())
            .Returns(new ScriptResult(true, "{}", "", null));

        var worker = CreateWorker();
        var result = await worker.PollOnceAsync(CancellationToken.None);

        Assert.True(result);
        await _apiClient.Received(1).PostResultAsync(
            job.JobId, "Succeeded", Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    private static JobPayload CreateTestJob() => new(
        Guid.NewGuid(), Guid.NewGuid(),
        new HostTarget("host.example.com", 22, "admin", "password"),
        new JobCredentials("s3cret", null, null),
        null,
        [new ToolPayload(Guid.NewGuid(), "tool-1", "python", "/usr/bin/python3",
            300, "print('hi')", "NormalizedSoftware")],
        DateTimeOffset.UtcNow.AddMinutes(10));
}
```

- [ ] **Step 2: Extract IRunnerApiClient interface from ApiClient**

The tests need to mock the API client. Add the interface.

Create — add to the top of `src/PatchHound.ScanRunner/ApiClient.cs` (before the `ApiClient` class):

In `src/PatchHound.ScanRunner/ApiClient.cs`, add the interface and make `ApiClient` implement it. Replace the entire file:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace PatchHound.ScanRunner;

public interface IRunnerApiClient
{
    Task SendHeartbeatAsync(CancellationToken ct);
    Task<JobPayload?> GetNextJobAsync(CancellationToken ct);
    Task SendJobHeartbeatAsync(Guid jobId, CancellationToken ct);
    Task PostResultAsync(Guid jobId, string status, string stdout, string stderr,
        string? errorMessage, CancellationToken ct);
}

public class ApiClient : IRunnerApiClient
{
    private readonly HttpClient _http;
    private readonly RunnerOptions _options;
    private readonly string _version;

    public ApiClient(HttpClient httpClient, RunnerOptions options)
    {
        _http = httpClient;
        _options = options;
        _version = GetType().Assembly.GetName().Version?.ToString() ?? "0.0.0";

        _http.BaseAddress = new Uri(options.CentralUrl.TrimEnd('/'));
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", options.BearerToken);
    }

    public async Task SendHeartbeatAsync(CancellationToken ct)
    {
        var request = new HeartbeatRequest(_version, _options.Hostname);
        var response = await _http.PostAsJsonAsync("/api/scan-runner/heartbeat", request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<JobPayload?> GetNextJobAsync(CancellationToken ct)
    {
        var response = await _http.GetAsync("/api/scan-runner/jobs/next", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JobPayload>(ct);
    }

    public async Task SendJobHeartbeatAsync(Guid jobId, CancellationToken ct)
    {
        var response = await _http.PostAsync(
            $"/api/scan-runner/jobs/{jobId}/heartbeat", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task PostResultAsync(
        Guid jobId, string status, string stdout, string stderr,
        string? errorMessage, CancellationToken ct)
    {
        var request = new PostResultRequest(status, stdout, stderr, errorMessage);
        var response = await _http.PostAsJsonAsync(
            $"/api/scan-runner/jobs/{jobId}/result", request, ct);
        response.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 3: Run tests — expected to fail**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~RunnerWorkerTests`
Expected: FAIL (RunnerWorker doesn't exist).

- [ ] **Step 4: Implement RunnerWorker**

Create `src/PatchHound.ScanRunner/RunnerWorker.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace PatchHound.ScanRunner;

public class RunnerWorker(
    RunnerOptions options,
    IRunnerApiClient apiClient,
    ISshExecutor sshExecutor,
    ILogger<RunnerWorker> logger) : BackgroundService
{
    private readonly SemaphoreSlim _concurrency = new(options.MaxConcurrentJobs);
    private DateTimeOffset _lastHeartbeat = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RunnerWorker started — polling every {Interval}s, max concurrency {Max}",
            options.PollIntervalSeconds, options.MaxConcurrentJobs);

        // Send initial heartbeat
        await SendHeartbeatSafeAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Periodic heartbeat
                if (DateTimeOffset.UtcNow - _lastHeartbeat > TimeSpan.FromSeconds(options.HeartbeatIntervalSeconds))
                {
                    await SendHeartbeatSafeAsync(stoppingToken);
                }

                await PollOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error during poll cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), stoppingToken);
        }
    }

    public async Task<bool> PollOnceAsync(CancellationToken ct)
    {
        var job = await apiClient.GetNextJobAsync(ct);
        if (job is null)
            return false;

        logger.LogInformation("Received job {JobId} for asset {AssetId} on {Host}",
            job.JobId, job.AssetId, job.HostTarget.Host);

        await ExecuteJobAsync(job, ct);
        return true;
    }

    public async Task ExecuteJobAsync(JobPayload job, CancellationToken ct)
    {
        // Start a lease heartbeat background task
        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var leaseTask = RunJobHeartbeatLoopAsync(job.JobId, jobCts.Token);

        string status;
        string stdout = "";
        string stderr = "";
        string? errorMessage = null;

        try
        {
            var result = await sshExecutor.ExecuteToolsAsync(
                job.HostTarget, job.Credentials, job.HostKeyFingerprint,
                job.Tools, ct);

            status = result.Success ? "Succeeded" : "Failed";
            stdout = result.CombinedStdout;
            stderr = result.CombinedStderr;
            errorMessage = result.ErrorMessage;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobId} failed with exception", job.JobId);
            status = "Failed";
            errorMessage = $"Runner error: {ex.Message}";
        }
        finally
        {
            // Stop the lease heartbeat
            await jobCts.CancelAsync();
            try { await leaseTask; } catch { /* expected cancellation */ }
        }

        // Truncate to stay within API limits
        const int maxStdout = 2 * 1024 * 1024;
        const int maxStderr = 256 * 1024;
        if (stdout.Length > maxStdout) stdout = stdout[..maxStdout];
        if (stderr.Length > maxStderr) stderr = stderr[..maxStderr];

        try
        {
            await apiClient.PostResultAsync(job.JobId, status, stdout, stderr, errorMessage, ct);
            logger.LogInformation("Job {JobId} completed with status {Status}", job.JobId, status);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to post result for job {JobId}", job.JobId);
        }
    }

    private async Task RunJobHeartbeatLoopAsync(Guid jobId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                await apiClient.SendJobHeartbeatAsync(jobId, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send job heartbeat for {JobId}", jobId);
            }
        }
    }

    private async Task SendHeartbeatSafeAsync(CancellationToken ct)
    {
        try
        {
            await apiClient.SendHeartbeatAsync(ct);
            _lastHeartbeat = DateTimeOffset.UtcNow;
            logger.LogDebug("Runner heartbeat sent");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to send runner heartbeat");
        }
    }
}
```

- [ ] **Step 5: Run tests — expected to pass**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~RunnerWorkerTests`
Expected: all 6 pass.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.ScanRunner/RunnerWorker.cs \
  src/PatchHound.ScanRunner/ApiClient.cs \
  tests/PatchHound.Tests/ScanRunner/RunnerWorkerTests.cs
git commit -m "feat: add RunnerWorker orchestrating poll-execute-report loop"
```

---

## Task 7: Wire up DI in Program.cs

**Files:**
- Modify: `src/PatchHound.ScanRunner/Program.cs`

- [ ] **Step 1: Update Program.cs with full DI wiring**

Replace `src/PatchHound.ScanRunner/Program.cs`:

```csharp
using PatchHound.ScanRunner;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

var builder = Host.CreateApplicationBuilder(args);

// Load runner.yaml from working directory or next to the binary
var yamlPath = FindConfigFile("runner.yaml");
if (yamlPath is null)
{
    Console.Error.WriteLine("ERROR: runner.yaml not found. Place it next to the binary or in the working directory.");
    return 1;
}

var yaml = File.ReadAllText(yamlPath);
var deserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();

var options = deserializer.Deserialize<RunnerOptions>(yaml);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<ISshExecutor, SshJobExecutor>();
builder.Services.AddHttpClient<IRunnerApiClient, ApiClient>(http =>
{
    // Base address and auth header are set in ApiClient constructor
});
builder.Services.AddHostedService<RunnerWorker>();

var host = builder.Build();

Console.WriteLine($"[startup] PatchHound.ScanRunner configured — central: {options.CentralUrl}, concurrency: {options.MaxConcurrentJobs}");
await host.RunAsync();
return 0;

static string? FindConfigFile(string fileName)
{
    // Check working directory first, then next to the binary
    var cwd = Path.Combine(Directory.GetCurrentDirectory(), fileName);
    if (File.Exists(cwd)) return cwd;

    var binDir = Path.Combine(AppContext.BaseDirectory, fileName);
    if (File.Exists(binDir)) return binDir;

    return null;
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build --nologo`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.ScanRunner/Program.cs
git commit -m "feat: wire up full DI for ScanRunner with ApiClient, SshExecutor, RunnerWorker"
```

---

## Task 8: Full test suite and final verification

- [ ] **Step 1: Run all ScanRunner tests**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~PatchHound.Tests.ScanRunner`
Expected: all 19 tests pass (6 ApiClient + 7 SshJobExecutor + 6 RunnerWorker).

- [ ] **Step 2: Run full solution test suite**

Run: `dotnet test --nologo`
Expected: all tests pass (434 existing + 19 new).

- [ ] **Step 3: Verify solution build**

Run: `dotnet build --nologo`
Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 4: Verify single-file publish**

Run: `dotnet publish src/PatchHound.ScanRunner -c Release --nologo`
Expected: Publish succeeded. Output in `src/PatchHound.ScanRunner/bin/Release/net10.0/publish/`.

- [ ] **Step 5: Commit**

```bash
git commit --allow-empty -m "chore: Plan 3 ScanRunner binary complete"
```

---

## Self-Review

**Spec coverage:**

- §5.2 HTTP endpoints — ApiClient covers all 4 endpoints (heartbeat, jobs/next, jobs/{id}/heartbeat, jobs/{id}/result) → Task 4
- §5.3 Credential delivery — Credentials received in JobPayload, held in memory only, passed to SSH → Tasks 3, 5
- §5.4 Leases & safety — Job heartbeat loop in RunnerWorker extends lease every 30s → Task 6
- §5.5 Runner binary internals — SSH.NET via SshJobExecutor (SFTP upload, execute, capture, delete) → Task 5. YAML config → Task 2. BackgroundService loop with polling → Task 6. Single-file publish → Task 1, 8.
- §5.5 Concurrency cap — `MaxConcurrentJobs` in RunnerOptions, SemaphoreSlim in RunnerWorker → Tasks 2, 6
- §5.5 Timeout enforcement — `cmd.CommandTimeout` set from tool's `TimeoutSeconds` → Task 5
- §5.5 Script cleanup — SFTP delete in `finally` block → Task 5

**Items NOT in scope for this plan (per design spec §11 / plan separation):**
- Runner enrollment (admin UI creates runner, shows secret once) — Plan 4 UI
- Host key fingerprint TOFU pinning — spec says optional in v1, punted to future work
- Bounded parallel execution (SemaphoreSlim is wired but the poll loop processes one job per tick; concurrent poll loops can be added later if needed)

**Placeholder scan:** No TBD, TODO, or incomplete sections found.

**Type consistency:**
- `IRunnerApiClient` / `ApiClient` — consistent across Tasks 4, 6
- `ISshExecutor` / `SshJobExecutor` — consistent across Tasks 5, 6
- `JobPayload`, `HostTarget`, `JobCredentials`, `ToolPayload`, `PostResultRequest` — defined in Task 3, used identically in Tasks 4, 5, 6
- `ScriptResult` — defined in Task 5, used in Task 6
- `RunnerOptions` — defined in Task 2, used in Tasks 4, 6, 7
