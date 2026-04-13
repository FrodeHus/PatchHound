using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.AuthenticatedScans;

public class ScanJobDispatcher(PatchHoundDbContext db)
{
    public async Task<Guid> StartRunAsync(
        Guid scanProfileId, string triggerKind, Guid? triggeredByUserId, CancellationToken ct)
    {
        var profile = await db.ScanProfiles.SingleAsync(p => p.Id == scanProfileId, ct);

        var hasActiveRun = await db.AuthenticatedScanRuns.AnyAsync(r =>
            r.ScanProfileId == scanProfileId
            && r.Status != AuthenticatedScanRunStatuses.Succeeded
            && r.Status != AuthenticatedScanRunStatuses.Failed
            && r.Status != AuthenticatedScanRunStatuses.PartiallyFailed, ct);
        if (hasActiveRun)
            throw new InvalidOperationException($"Scan profile {scanProfileId} already has an active run");

        var now = DateTimeOffset.UtcNow;
        var run = AuthenticatedScanRun.Start(profile.TenantId, scanProfileId, triggerKind, triggeredByUserId, now);
        db.AuthenticatedScanRuns.Add(run);

        // Snapshot current tool version IDs
        var toolIds = await db.ScanProfileTools
            .Where(t => t.ScanProfileId == scanProfileId)
            .OrderBy(t => t.ExecutionOrder)
            .Select(t => t.ScanningToolId)
            .ToListAsync(ct);
        var versionIds = await db.ScanningTools
            .Where(t => toolIds.Contains(t.Id) && t.CurrentVersionId != null)
            .Select(t => t.CurrentVersionId!.Value)
            .ToListAsync(ct);
        var versionIdsJson = JsonSerializer.Serialize(versionIds);

        // Assigned devices
        var deviceIds = await db.DeviceScanProfileAssignments
            .Where(a => a.ScanProfileId == scanProfileId)
            .Select(a => a.DeviceId)
            .ToListAsync(ct);

        if (deviceIds.Count == 0)
        {
            run.MarkRunning(0);
            run.Complete(0, 0, 0, now);
            await db.SaveChangesAsync(ct);
            return run.Id;
        }

        foreach (var deviceId in deviceIds)
        {
            db.ScanJobs.Add(ScanJob.Create(
                profile.TenantId, run.Id, profile.ScanRunnerId,
                deviceId, profile.ConnectionProfileId, versionIdsJson));
        }

        run.MarkRunning(deviceIds.Count);
        profile.RecordRunStarted(now);
        profile.ClearManualRequest();

        await db.SaveChangesAsync(ct);
        return run.Id;
    }
}
