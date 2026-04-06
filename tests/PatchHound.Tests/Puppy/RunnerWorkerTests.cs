using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PatchHound.Puppy;
using Xunit;

namespace PatchHound.Tests.Puppy;

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
