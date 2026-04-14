using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class RemediationAiJobService(PatchHoundDbContext dbContext)
{
    public async Task<Result<RemediationAiJob>> EnqueueAsync(
        Guid tenantId,
        Guid remediationCaseId,
        string inputHash,
        CancellationToken ct
    )
    {
        var caseExists = await dbContext.RemediationCases
            .AnyAsync(item => item.TenantId == tenantId && item.Id == remediationCaseId, ct);
        if (!caseExists)
        {
            return Result<RemediationAiJob>.Failure("Remediation case was not found.");
        }

        var existingActiveJob = await dbContext.RemediationAiJobs
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId
                && item.RemediationCaseId == remediationCaseId
                && (
                    item.Status == RemediationAiJobStatus.Pending
                    || item.Status == RemediationAiJobStatus.Running
                ))
            .OrderByDescending(item => item.RequestedAt)
            .FirstOrDefaultAsync(ct);
        if (existingActiveJob is not null)
        {
            if (!string.Equals(existingActiveJob.InputHash, inputHash, StringComparison.Ordinal))
            {
                existingActiveJob.Refresh(inputHash, DateTimeOffset.UtcNow);
                await dbContext.SaveChangesAsync(ct);
            }

            return Result<RemediationAiJob>.Success(existingActiveJob);
        }

        var latestCompletedJob = await dbContext.RemediationAiJobs
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.RemediationCaseId == remediationCaseId)
            .OrderByDescending(item => item.RequestedAt)
            .FirstOrDefaultAsync(ct);
        if (latestCompletedJob is not null
            && string.Equals(latestCompletedJob.InputHash, inputHash, StringComparison.Ordinal)
            && latestCompletedJob.Status == RemediationAiJobStatus.Succeeded)
        {
            return Result<RemediationAiJob>.Success(latestCompletedJob);
        }

        var job = RemediationAiJob.Create(tenantId, remediationCaseId, inputHash, DateTimeOffset.UtcNow);
        await dbContext.RemediationAiJobs.AddAsync(job, ct);
        await dbContext.SaveChangesAsync(ct);

        return Result<RemediationAiJob>.Success(job);
    }

    public Task<RemediationAiJob?> GetLatestAsync(Guid tenantId, Guid remediationCaseId, CancellationToken ct)
    {
        return dbContext.RemediationAiJobs
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.RemediationCaseId == remediationCaseId)
            .OrderByDescending(item => item.RequestedAt)
            .FirstOrDefaultAsync(ct);
    }
}
