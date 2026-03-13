using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class EnrichmentJobEnqueuer(
    PatchHoundDbContext dbContext,
    ILogger<EnrichmentJobEnqueuer> logger
)
{
    public async Task EnqueueVulnerabilityJobsAsync(
        Guid tenantId,
        IReadOnlyList<Guid> vulnerabilityDefinitionIds,
        CancellationToken ct
    )
    {
        if (vulnerabilityDefinitionIds.Count == 0)
        {
            return;
        }

        var enabledSourceKeys = await dbContext
            .EnrichmentSourceConfigurations.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(source => source.Enabled && !string.IsNullOrWhiteSpace(source.SecretRef))
            .Select(source => source.SourceKey)
            .ToListAsync(ct);

        if (enabledSourceKeys.Count == 0)
        {
            return;
        }

        var definitions = await dbContext
            .VulnerabilityDefinitions.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(definition => vulnerabilityDefinitionIds.Contains(definition.Id))
            .Select(definition => new
            {
                definition.Id,
                definition.ExternalId,
                definition.Source,
                definition.Description,
                definition.CvssScore,
                definition.CvssVector,
                definition.PublishedDate,
                ReferenceCount = definition.References.Count,
                AffectedSoftwareCount = definition.AffectedSoftware.Count,
            })
            .ToListAsync(ct);

        if (definitions.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var existingJobs = await dbContext
            .EnrichmentJobs.IgnoreQueryFilters()
            .Where(job =>
                job.TargetModel == EnrichmentTargetModel.Vulnerability
                && vulnerabilityDefinitionIds.Contains(job.TargetId)
            )
            .ToDictionaryAsync(job => (job.SourceKey, job.TargetId), ct);

        var createdCount = 0;
        var refreshedCount = 0;

        foreach (var definition in definitions)
        {
            if (
                !ShouldEnqueueVulnerability(
                    definition.ExternalId,
                    definition.Source,
                    definition.Description,
                    definition.CvssScore,
                    definition.CvssVector,
                    definition.PublishedDate,
                    definition.ReferenceCount,
                    definition.AffectedSoftwareCount
                )
            )
            {
                continue;
            }

            foreach (var sourceKey in enabledSourceKeys)
            {
                var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
                var key = (normalizedSourceKey, definition.Id);

                if (existingJobs.TryGetValue(key, out var existingJob))
                {
                    if (existingJob.Status == EnrichmentJobStatus.Running)
                    {
                        continue;
                    }

                    existingJob.Refresh(priority: 100, nextAttemptAt: now);
                    refreshedCount++;
                    continue;
                }

                var job = EnrichmentJob.Create(
                    tenantId,
                    normalizedSourceKey,
                    EnrichmentTargetModel.Vulnerability,
                    definition.Id,
                    definition.ExternalId,
                    priority: 100,
                    queuedAt: now
                );
                await dbContext.EnrichmentJobs.AddAsync(job, ct);
                existingJobs[key] = job;
                createdCount++;
            }
        }

        if (createdCount == 0 && refreshedCount == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation(
            "Queued enrichment jobs for tenant {TenantId}: created={CreatedCount} refreshed={RefreshedCount}.",
            tenantId,
            createdCount,
            refreshedCount
        );
    }

    private static bool ShouldEnqueueVulnerability(
        string externalId,
        string source,
        string description,
        decimal? cvssScore,
        string? cvssVector,
        DateTimeOffset? publishedDate,
        int referenceCount,
        int affectedSoftwareCount
    )
    {
        if (!externalId.StartsWith("CVE-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hasNvd = source
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => string.Equals(item, "NVD", StringComparison.OrdinalIgnoreCase));

        if (!hasNvd)
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(description)
            || !cvssScore.HasValue
            || string.IsNullOrWhiteSpace(cvssVector)
            || !publishedDate.HasValue
            || referenceCount == 0
            || affectedSoftwareCount == 0;
    }
}
