using PatchHound.Puppy;
using Xunit;

namespace PatchHound.Tests.Puppy;

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
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var pem = rsa.ExportRSAPrivateKeyPem();
        return pem;
    }
}
