using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class SoftwareDescriptionJobService(PatchHoundDbContext dbContext)
{
    public async Task<Result<SoftwareDescriptionJob>> EnqueueAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
        Guid? tenantAiProfileId,
        CancellationToken ct
    )
    {
        var tenantSoftware = await dbContext
            .TenantSoftware
            .Where(item => item.Id == tenantSoftwareId && item.TenantId == tenantId)
            .Select(item => new { item.Id, item.NormalizedSoftwareId })
            .FirstOrDefaultAsync(ct);
        if (tenantSoftware is null)
        {
            return Result<SoftwareDescriptionJob>.Failure("Tenant software was not found.");
        }

        if (tenantAiProfileId.HasValue)
        {
            var aiProfileExists = await dbContext.TenantAiProfiles.AnyAsync(
                item => item.Id == tenantAiProfileId.Value && item.TenantId == tenantId,
                ct
            );
            if (!aiProfileExists)
            {
                return Result<SoftwareDescriptionJob>.Failure("The requested AI profile was not found.");
            }
        }

        var existingActiveJob = await dbContext
            .Set<SoftwareDescriptionJob>()
            .Where(item =>
                item.TenantId == tenantId
                && item.TenantSoftwareId == tenantSoftwareId
                && (
                    item.Status == SoftwareDescriptionJobStatus.Pending
                    || item.Status == SoftwareDescriptionJobStatus.Running
                )
            )
            .OrderByDescending(item => item.RequestedAt)
            .FirstOrDefaultAsync(ct);
        if (existingActiveJob is not null)
        {
            return Result<SoftwareDescriptionJob>.Success(existingActiveJob);
        }

        var job = SoftwareDescriptionJob.Create(
            tenantId,
            tenantSoftwareId,
            tenantSoftware.NormalizedSoftwareId,
            tenantAiProfileId,
            DateTimeOffset.UtcNow
        );
        await dbContext.AddAsync(job, ct);
        await dbContext.SaveChangesAsync(ct);

        return Result<SoftwareDescriptionJob>.Success(job);
    }

    public Task<SoftwareDescriptionJob?> GetLatestAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
        CancellationToken ct
    )
    {
        return dbContext
            .Set<SoftwareDescriptionJob>()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.TenantSoftwareId == tenantSoftwareId)
            .OrderByDescending(item => item.RequestedAt)
            .FirstOrDefaultAsync(ct);
    }
}
