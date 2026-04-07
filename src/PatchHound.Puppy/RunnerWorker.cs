using Microsoft.Extensions.Logging;

namespace PatchHound.Puppy;

public class RunnerWorker(
    RunnerOptions options,
    IRunnerApiClient apiClient,
    ISshExecutor sshExecutor,
    ILogger<RunnerWorker> logger) : BackgroundService
{
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
