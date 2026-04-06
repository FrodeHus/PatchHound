using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.AuthenticatedScans;

public class ScanRunCompletionService(PatchHoundDbContext db)
{
    private static readonly HashSet<string> TerminalStatuses =
    [
        ScanJobStatuses.Succeeded,
        ScanJobStatuses.Failed,
        ScanJobStatuses.TimedOut
    ];

    public async Task TryCompleteRunAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.AuthenticatedScanRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.CompletedAt.HasValue) return;

        var jobs = await db.ScanJobs.Where(j => j.RunId == runId).ToListAsync(ct);
        if (jobs.Count == 0) return;

        var allTerminal = jobs.All(j => TerminalStatuses.Contains(j.Status));
        if (!allTerminal) return;

        var succeeded = jobs.Count(j => j.Status == ScanJobStatuses.Succeeded);
        var failed = jobs.Count - succeeded;
        var entriesIngested = jobs
            .Where(j => j.Status == ScanJobStatuses.Succeeded)
            .Sum(j => j.EntriesIngested);

        run.Complete(succeeded, failed, entriesIngested, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
    }
}
