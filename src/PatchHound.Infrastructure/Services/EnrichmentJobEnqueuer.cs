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
                    : !string.IsNullOrWhiteSpace(source.SecretRef))
            .Select(source => source.SourceKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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
                HasDefenderReference = definition.References.Any(reference =>
                    reference.Source == "MicrosoftDefender"),
                DefenderLastRefreshedAt = dbContext.VulnerabilityThreatAssessments
                    .Where(assessment => assessment.VulnerabilityDefinitionId == definition.Id)
                    .Select(assessment => assessment.DefenderLastRefreshedAt)
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
}
