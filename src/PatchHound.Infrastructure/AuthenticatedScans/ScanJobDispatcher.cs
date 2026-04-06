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

        // Assigned assets
        var assetIds = await db.AssetScanProfileAssignments
            .Where(a => a.ScanProfileId == scanProfileId)
            .Select(a => a.AssetId)
            .ToListAsync(ct);

        if (assetIds.Count == 0)
        {
            run.MarkRunning(0);
            run.Complete(0, 0, 0, now);
            await db.SaveChangesAsync(ct);
            return run.Id;
        }

        foreach (var assetId in assetIds)
        {
            db.ScanJobs.Add(ScanJob.Create(
                profile.TenantId, run.Id, profile.ScanRunnerId,
                assetId, profile.ConnectionProfileId, versionIdsJson));
        }

        run.MarkRunning(assetIds.Count);
        profile.RecordRunStarted(now);
        profile.ClearManualRequest();

        await db.SaveChangesAsync(ct);
        return run.Id;
    }
}
