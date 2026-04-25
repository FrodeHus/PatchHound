using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Infrastructure.Services;

public class EnrichmentJobEnqueuer(
    PatchHoundDbContext dbContext,
    ILogger<EnrichmentJobEnqueuer> logger
)
{
    internal static readonly TimeSpan DefaultDefenderRefreshTtl = TimeSpan.FromHours(
        EnrichmentSourceCatalog.DefaultDefenderRefreshTtlHours
    );

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

        var enabledSources = await dbContext
            .EnrichmentSourceConfigurations.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(source => source.Enabled)
            .Select(source => new { source.SourceKey, source.SecretRef, source.RefreshTtlHours })
            .ToListAsync(ct);

        if (enabledSources.Count == 0)
        {
            return;
        }

        var defenderConfiguredForTenant = await dbContext
            .TenantSourceConfigurations.IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                source =>
                    source.TenantId == tenantId
                    && source.SourceKey == TenantSourceCatalog.DefenderSourceKey
                    && source.Enabled
                    && !string.IsNullOrWhiteSpace(source.CredentialTenantId)
                    && !string.IsNullOrWhiteSpace(source.ClientId)
                    && !string.IsNullOrWhiteSpace(source.SecretRef),
                ct
            );

        var enabledSourceKeys = enabledSources
            .Where(source =>
                string.Equals(source.SourceKey, EnrichmentSourceCatalog.DefenderSourceKey, StringComparison.OrdinalIgnoreCase)
                    ? defenderConfiguredForTenant
                    : string.Equals(source.SourceKey, EnrichmentSourceCatalog.NvdSourceKey, StringComparison.OrdinalIgnoreCase)
                        || !string.IsNullOrWhiteSpace(source.SecretRef))
            .Select(source => source.SourceKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (enabledSourceKeys.Count == 0)
        {
            return;
        }

        var definitions = await dbContext
            .Vulnerabilities.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => vulnerabilityDefinitionIds.Contains(v.Id))
            .Select(v => new
            {
                v.Id,
                v.ExternalId,
                v.Source,
                v.Description,
                v.CvssScore,
                v.CvssVector,
                v.PublishedDate,
                ReferenceCount = dbContext.VulnerabilityReferences.Count(r => r.VulnerabilityId == v.Id),
                AffectedSoftwareCount = dbContext.VulnerabilityApplicabilities.Count(a => a.VulnerabilityId == v.Id),
                HasDefenderReference = dbContext.VulnerabilityReferences.Any(r =>
                    r.VulnerabilityId == v.Id && r.Source == "MicrosoftDefender"),
                DefenderLastRefreshedAt = dbContext.ThreatAssessments
                    .Where(ta => ta.VulnerabilityId == v.Id)
                    .Select(ta => ta.DefenderLastRefreshedAt)
                    .FirstOrDefault(),
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
            foreach (var sourceKey in enabledSourceKeys)
            {
                var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
                var sourceConfig = enabledSources.First(source =>
                    string.Equals(source.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase)
                );
                var refreshTtl =
                    string.Equals(
                        normalizedSourceKey,
                        EnrichmentSourceCatalog.DefenderSourceKey,
                        StringComparison.OrdinalIgnoreCase
                    ) && sourceConfig.RefreshTtlHours.GetValueOrDefault() > 0
                        ? TimeSpan.FromHours(sourceConfig.RefreshTtlHours!.Value)
                        : DefaultDefenderRefreshTtl;
                if (
                    !ShouldEnqueueVulnerability(
                        normalizedSourceKey,
                        definition.ExternalId,
                        definition.Source,
                        definition.Description,
                        definition.CvssScore,
                        definition.CvssVector,
                        definition.PublishedDate,
                        definition.ReferenceCount,
                        definition.AffectedSoftwareCount,
                        definition.HasDefenderReference,
                        definition.DefenderLastRefreshedAt,
                        now,
                        refreshTtl
                    )
                )
                {
                    continue;
                }

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
        string sourceKey,
        string externalId,
        string source,
        string description,
        decimal? cvssScore,
        string? cvssVector,
        DateTimeOffset? publishedDate,
        int referenceCount,
        int affectedSoftwareCount,
        bool hasDefenderReference,
        DateTimeOffset? defenderLastRefreshedAt,
        DateTimeOffset now,
        TimeSpan defenderRefreshTtl
    )
    {
        if (!externalId.StartsWith("CVE-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (
            string.Equals(
                sourceKey,
                EnrichmentSourceCatalog.DefenderSourceKey,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return !hasDefenderReference
                || !defenderLastRefreshedAt.HasValue
                || now - defenderLastRefreshedAt.Value >= defenderRefreshTtl;
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

    public async Task EnqueueSoftwareEndOfLifeJobsAsync(
        Guid tenantId,
        IReadOnlyList<Guid> normalizedSoftwareIds,
        CancellationToken ct
    )
    {
        if (normalizedSoftwareIds.Count == 0)
        {
            return;
        }

        var eolEnabled = await dbContext
            .EnrichmentSourceConfigurations.IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                source =>
                    source.SourceKey == EnrichmentSourceCatalog.EndOfLifeSourceKey
                    && source.Enabled,
                ct
            );

        if (!eolEnabled)
        {
            return;
        }

        var softwareItems = await dbContext
            .SoftwareProducts.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(software => normalizedSoftwareIds.Contains(software.Id))
            .Select(software => new
            {
                software.Id,
                software.CanonicalProductKey,
                software.EolEnrichedAt,
            })
            .ToListAsync(ct);

        if (softwareItems.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var existingJobs = await dbContext
            .EnrichmentJobs.IgnoreQueryFilters()
            .Where(job =>
                job.SourceKey == EnrichmentSourceCatalog.EndOfLifeSourceKey
                && job.TargetModel == EnrichmentTargetModel.SoftwareAsset
                && normalizedSoftwareIds.Contains(job.TargetId)
            )
            .ToDictionaryAsync(job => job.TargetId, ct);

        var createdCount = 0;
        var refreshedCount = 0;

        foreach (var software in softwareItems)
        {
            // Skip if already enriched within the last 7 days
            if (software.EolEnrichedAt.HasValue && now - software.EolEnrichedAt.Value < TimeSpan.FromDays(7))
            {
                continue;
            }

            if (existingJobs.TryGetValue(software.Id, out var existingJob))
            {
                if (existingJob.Status == EnrichmentJobStatus.Running)
                {
                    continue;
                }

                existingJob.Refresh(priority: 50, nextAttemptAt: now);
                refreshedCount++;
                continue;
            }

            var job = EnrichmentJob.Create(
                tenantId,
                EnrichmentSourceCatalog.EndOfLifeSourceKey,
                EnrichmentTargetModel.SoftwareAsset,
                software.Id,
                software.CanonicalProductKey,
                priority: 50,
                queuedAt: now
            );
            await dbContext.EnrichmentJobs.AddAsync(job, ct);
            existingJobs[software.Id] = job;
            createdCount++;
        }

        if (createdCount == 0 && refreshedCount == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation(
            "Queued end-of-life enrichment jobs for tenant {TenantId}: created={CreatedCount} refreshed={RefreshedCount}.",
            tenantId,
            createdCount,
            refreshedCount
        );
    }

    public async Task EnqueueSoftwareSupplyChainJobsAsync(
        Guid tenantId,
        IReadOnlyList<Guid> normalizedSoftwareIds,
        CancellationToken ct
    )
    {
        if (normalizedSoftwareIds.Count == 0)
        {
            return;
        }

        var sourceConfig = await dbContext
            .EnrichmentSourceConfigurations.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                source =>
                    source.SourceKey == EnrichmentSourceCatalog.SupplyChainSourceKey
                    && source.Enabled,
                ct
            );

        if (sourceConfig is null || string.IsNullOrWhiteSpace(sourceConfig.ApiBaseUrl))
        {
            return;
        }

        var refreshTtl = sourceConfig.RefreshTtlHours.GetValueOrDefault(
            EnrichmentSourceCatalog.DefaultSupplyChainRefreshTtlHours
        );
        var refreshWindow = TimeSpan.FromHours(refreshTtl <= 0
            ? EnrichmentSourceCatalog.DefaultSupplyChainRefreshTtlHours
            : refreshTtl);

        var softwareItems = await dbContext
            .SoftwareProducts.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(software => normalizedSoftwareIds.Contains(software.Id))
            .Select(software => new
            {
                software.Id,
                software.CanonicalProductKey,
                software.SupplyChainEnrichedAt,
            })
            .ToListAsync(ct);

        if (softwareItems.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var existingJobs = await dbContext
            .EnrichmentJobs.IgnoreQueryFilters()
            .Where(job =>
                job.SourceKey == EnrichmentSourceCatalog.SupplyChainSourceKey
                && job.TargetModel == EnrichmentTargetModel.SoftwareAsset
                && normalizedSoftwareIds.Contains(job.TargetId)
            )
            .ToDictionaryAsync(job => job.TargetId, ct);

        var createdCount = 0;
        var refreshedCount = 0;

        foreach (var software in softwareItems)
        {
            if (
                software.SupplyChainEnrichedAt.HasValue
                && now - software.SupplyChainEnrichedAt.Value < refreshWindow
            )
            {
                continue;
            }

            if (existingJobs.TryGetValue(software.Id, out var existingJob))
            {
                if (existingJob.Status == EnrichmentJobStatus.Running)
                {
                    continue;
                }

                existingJob.Refresh(priority: 45, nextAttemptAt: now);
                refreshedCount++;
                continue;
            }

            var job = EnrichmentJob.Create(
                tenantId,
                EnrichmentSourceCatalog.SupplyChainSourceKey,
                EnrichmentTargetModel.SoftwareAsset,
                software.Id,
                software.CanonicalProductKey,
                priority: 45,
                queuedAt: now
            );
            await dbContext.EnrichmentJobs.AddAsync(job, ct);
            existingJobs[software.Id] = job;
            createdCount++;
        }

        if (createdCount == 0 && refreshedCount == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation(
            "Queued supply-chain enrichment jobs for tenant {TenantId}: created={CreatedCount} refreshed={RefreshedCount}.",
            tenantId,
            createdCount,
            refreshedCount
        );
    }
}
