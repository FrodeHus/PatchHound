using Cronos;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.AuthenticatedScans;

public class ScanSchedulerTickHandler(
    PatchHoundDbContext db,
    ScanJobDispatcher dispatcher,
    ScanRunCompletionService completionService)
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan StalePendingThreshold = TimeSpan.FromHours(2);

    public async Task TickAsync(CancellationToken ct)
    {
        await EvaluateCronProfilesAsync(ct);
        await SweepExpiredLeasesAsync(ct);
        await SweepStalePendingJobsAsync(ct);
    }

    private async Task EvaluateCronProfilesAsync(CancellationToken ct)
    {
        var profiles = await db.ScanProfiles
            .Where(p => p.Enabled && p.CronSchedule != "")
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;

        foreach (var profile in profiles)
        {
            try
            {
                var cron = CronExpression.Parse(profile.CronSchedule);
                var baseline = profile.LastRunStartedAt ?? profile.CreatedAt;
                var nextDue = cron.GetNextOccurrence(baseline.UtcDateTime, TimeZoneInfo.Utc);

                if (nextDue.HasValue && nextDue.Value <= now.UtcDateTime)
                {
                    await dispatcher.StartRunAsync(profile.Id, "scheduled", null, ct);
                }
            }
            catch
            {
                // Invalid cron or dispatch failure — skip this profile
            }
        }
    }

    private async Task SweepExpiredLeasesAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var expiredJobs = await db.ScanJobs
            .Where(j => j.Status == ScanJobStatuses.Dispatched
                && j.LeaseExpiresAt.HasValue
                && j.LeaseExpiresAt < now)
            .ToListAsync(ct);

        var affectedRunIds = new HashSet<Guid>();

        foreach (var job in expiredJobs)
        {
            if (job.AttemptCount >= MaxAttempts)
            {
                job.CompleteFailed(ScanJobStatuses.Failed,
                    "runner unreachable after 3 attempts", now);
                affectedRunIds.Add(job.RunId);
            }
            else
            {
                job.ReturnToPending("lease expired");
            }
        }

        if (expiredJobs.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        foreach (var runId in affectedRunIds)
        {
            await completionService.TryCompleteRunAsync(runId, ct);
        }
    }

    private async Task SweepStalePendingJobsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var threshold = now - StalePendingThreshold;

        var staleJobs = await (
            from job in db.ScanJobs
            join run in db.AuthenticatedScanRuns on job.RunId equals run.Id
            where job.Status == ScanJobStatuses.Pending
                && run.StartedAt < threshold
            select job
        ).ToListAsync(ct);

        var affectedRunIds = new HashSet<Guid>();

        foreach (var job in staleJobs)
        {
            job.CompleteFailed(ScanJobStatuses.Failed,
                "runner offline (never picked up)", now);
            affectedRunIds.Add(job.RunId);
        }

        if (staleJobs.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        foreach (var runId in affectedRunIds)
        {
            await completionService.TryCompleteRunAsync(runId, ct);
        }
    }
}
