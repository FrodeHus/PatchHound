using System.Text;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace PatchHound.Puppy;

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
