using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Infrastructure.AuthenticatedScans;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/scan-runner")]
[Authorize(AuthenticationSchemes = ScanRunnerBearerHandler.SchemeName)]
public class ScanRunnerController(
    PatchHoundDbContext db,
    ISecretStore secretStore,
    AuthenticatedScanIngestionService ingestionService,
    ScanRunCompletionService completionService) : ControllerBase
{
    private const int MaxStdoutBytes = 2 * 1024 * 1024;
    private const int MaxStderrBytes = 256 * 1024;

    public record HeartbeatRequest(string Version, string Hostname);
    public record PostResultRequest(string Status, string Stdout, string Stderr, string? ErrorMessage);

    public record JobPayload(
        Guid JobId, Guid AssetId,
        HostTarget HostTarget, Credentials Credentials,
        string? HostKeyFingerprint,
        List<ToolPayload> Tools,
        DateTimeOffset LeaseExpiresAt);

    public record HostTarget(string Host, int Port, string Username, string AuthMethod);
    public record Credentials(string? Password, string? PrivateKey, string? Passphrase);
    public record ToolPayload(
        Guid Id, string Name, string ScriptType, string InterpreterPath,
        int TimeoutSeconds, string ScriptContent, string OutputModel);

    private Guid GetRunnerId() =>
        Guid.Parse(User.FindFirstValue(ScanRunnerBearerHandler.RunnerIdClaim)!);
    private Guid GetTenantId() =>
        Guid.Parse(User.FindFirstValue(ScanRunnerBearerHandler.TenantIdClaim)!);

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat(
        [FromBody] HeartbeatRequest req, CancellationToken ct)
    {
        var runner = await db.ScanRunners.FirstOrDefaultAsync(
            r => r.Id == GetRunnerId(), ct);
        if (runner is null) return NotFound();

        runner.RecordHeartbeat(req.Version, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    [HttpGet("jobs/next")]
    public async Task<ActionResult<JobPayload>> GetNextJob(CancellationToken ct)
    {
        var runnerId = GetRunnerId();
        var tenantId = GetTenantId();

        var job = await db.ScanJobs
            .Where(j => j.TenantId == tenantId
                && j.ScanRunnerId == runnerId
                && j.Status == ScanJobStatuses.Pending)
            .OrderBy(j => j.Id)
            .FirstOrDefaultAsync(ct);

        if (job is null) return NoContent();

        // Load connection profile for credentials
        var connProfile = await db.ConnectionProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == job.ConnectionProfileId, ct);
        if (connProfile is null)
        {
            return StatusCode(500, new { error = "Connection profile not found" });
        }

        // Fetch credentials JIT from OpenBao
        string? password = null, privateKey = null, passphrase = null;
        try
        {
            if (connProfile.AuthMethod == "password")
            {
                password = await secretStore.GetSecretAsync(connProfile.SecretRef, "password", ct);
            }
            else if (connProfile.AuthMethod == "privateKey")
            {
                privateKey = await secretStore.GetSecretAsync(connProfile.SecretRef, "privateKey", ct);
                passphrase = await secretStore.GetSecretAsync(connProfile.SecretRef, "passphrase", ct);
            }
        }
        catch
        {
            return StatusCode(503, new { error = "Credential store unavailable" });
        }

        // Load tool versions
        var versionIds = JsonSerializer.Deserialize<List<Guid>>(job.ScanningToolVersionIdsJson) ?? [];
        var tools = await (
            from v in db.ScanningToolVersions.AsNoTracking()
            join t in db.ScanningTools.AsNoTracking() on v.ScanningToolId equals t.Id
            where versionIds.Contains(v.Id)
            select new ToolPayload(t.Id, t.Name, t.ScriptType, t.InterpreterPath,
                t.TimeoutSeconds, v.ScriptContent, t.OutputModel)
        ).ToListAsync(ct);

        // Dispatch the job
        var leaseExpiry = DateTimeOffset.UtcNow.AddMinutes(10);
        job.Dispatch(leaseExpiry);
        await db.SaveChangesAsync(ct);

        return Ok(new JobPayload(
            job.Id, job.AssetId,
            new HostTarget(connProfile.SshHost, connProfile.SshPort, connProfile.SshUsername, connProfile.AuthMethod),
            new Credentials(password, privateKey, passphrase),
            connProfile.HostKeyFingerprint,
            tools,
            leaseExpiry));
    }

    [HttpPost("jobs/{jobId:guid}/heartbeat")]
    public async Task<IActionResult> JobHeartbeat(Guid jobId, CancellationToken ct)
    {
        var job = await db.ScanJobs.FirstOrDefaultAsync(
            j => j.Id == jobId && j.ScanRunnerId == GetRunnerId(), ct);
        if (job is null) return NotFound();

        job.Dispatch(DateTimeOffset.UtcNow.AddMinutes(10));
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    [HttpPost("jobs/{jobId:guid}/result")]
    public async Task<IActionResult> PostResult(
        Guid jobId, [FromBody] PostResultRequest req, CancellationToken ct)
    {
        if (req.Stdout.Length > MaxStdoutBytes)
            return StatusCode(413, new { error = $"stdout exceeds {MaxStdoutBytes} bytes" });
        if (req.Stderr.Length > MaxStderrBytes)
            return StatusCode(413, new { error = $"stderr exceeds {MaxStderrBytes} bytes" });

        var job = await db.ScanJobs.FirstOrDefaultAsync(
            j => j.Id == jobId && j.ScanRunnerId == GetRunnerId(), ct);
        if (job is null) return NotFound();

        if (req.Status == "Succeeded")
        {
            await ingestionService.ProcessJobResultAsync(jobId, req.Stdout, req.Stderr, ct);
            // ProcessJobResultAsync already marks the job succeeded
        }
        else
        {
            job.CompleteFailed(req.Status, req.ErrorMessage ?? "Unknown error", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
        }

        await completionService.TryCompleteRunAsync(job.RunId, ct);
        return Ok(new { ok = true });
    }
}
