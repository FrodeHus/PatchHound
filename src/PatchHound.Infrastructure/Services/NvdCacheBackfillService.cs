using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Infrastructure.Services;

public class NvdCacheBackfillService(
    PatchHoundDbContext db,
    VulnerabilityResolver resolver,
    ILogger<NvdCacheBackfillService> logger)
{
    public const int DefaultBatchSize = 500;

    public async Task<NvdBackfillStats> RunAsync(
        int batchSize = DefaultBatchSize, CancellationToken ct = default)
    {
        var items = await (
            from v in db.Vulnerabilities.IgnoreQueryFilters()
            join c in db.NvdCveCache on v.ExternalId equals c.CveId
            where v.ExternalId.StartsWith("CVE-")
                && ((v.Description == null || v.Description == "")
                    || v.CvssScore == null
                    || (v.CvssVector == null || v.CvssVector == "")
                    || v.PublishedDate == null
                    || !db.VulnerabilityReferences.Any(r => r.VulnerabilityId == v.Id)
                    || !db.VulnerabilityApplicabilities.Any(a => a.VulnerabilityId == v.Id))
            select new { v.ExternalId, Cache = c }
        ).OrderBy(x => x.ExternalId).Take(batchSize).AsNoTracking().ToListAsync(ct);

        if (items.Count == 0)
            return new NvdBackfillStats(0, 0, 0);

        var aliasMap = await LoadAliasMapAsync(ct);

        db.SetSystemContext(true);
        var succeeded = 0;
        var failed = 0;
        try
        {
            foreach (var item in items)
            {
                try
                {
                    var input = BuildResolveInput(item.ExternalId, item.Cache, aliasMap);
                    await resolver.ResolveAsync(input, ct);
                    await db.SaveChangesAsync(ct);
                    succeeded++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex,
                        "NVD backfill failed for {CveId}", item.ExternalId);
                    db.ChangeTracker.Clear();
                    failed++;
                }
            }
        }
        finally
        {
            db.SetSystemContext(false);
        }

        return new NvdBackfillStats(items.Count, succeeded, failed);
    }

    private async Task<Dictionary<string, Guid>> LoadAliasMapAsync(CancellationToken ct)
    {
        var sourceSystemId = await db.SourceSystems
            .Where(s => s.Key == EnrichmentSourceCatalog.NvdSourceKey)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);

        if (sourceSystemId is null)
            return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        return await db.SoftwareAliases
            .Where(a => a.SourceSystemId == sourceSystemId)
            .ToDictionaryAsync(a => a.ExternalId, a => a.SoftwareProductId,
                StringComparer.OrdinalIgnoreCase, ct);
    }

    internal static VulnerabilityResolveInput BuildResolveInput(
        string cveId,
        NvdCveCache cached,
        IReadOnlyDictionary<string, Guid> aliasMap)
    {
        var refs = JsonSerializer.Deserialize<List<NvdCachedReference>>(
            cached.ReferencesJson) ?? [];
        var cpes = JsonSerializer.Deserialize<List<NvdCachedCpeMatch>>(
            cached.ConfigurationsJson) ?? [];

        var references = refs
            .Where(r => !string.IsNullOrWhiteSpace(r.Url))
            .Select(r => new VulnerabilityReferenceInput(
                r.Url,
                string.IsNullOrWhiteSpace(r.Source) ? "NVD" : r.Source,
                (IReadOnlyList<string>)r.Tags))
            .ToList();

        var applicabilities = cpes
            .Where(m => !string.IsNullOrWhiteSpace(m.Criteria))
            .Select(m => new VulnerabilityApplicabilityInput(
                SoftwareProductId: aliasMap.TryGetValue(m.Criteria, out var pid) ? pid : null,
                CpeCriteria: m.Criteria,
                Vulnerable: m.Vulnerable,
                VersionStartIncluding: m.VersionStartIncluding,
                VersionStartExcluding: m.VersionStartExcluding,
                VersionEndIncluding: m.VersionEndIncluding,
                VersionEndExcluding: m.VersionEndExcluding))
            .ToList();

        var severity = cached.CvssScore switch
        {
            null => Severity.Low,
            >= 9.0m => Severity.Critical,
            >= 7.0m => Severity.High,
            >= 4.0m => Severity.Medium,
            _ => Severity.Low,
        };

        return new VulnerabilityResolveInput(
            Source: "nvd",
            ExternalId: cveId,
            Title: cveId,
            Description: cached.Description,
            VendorSeverity: severity,
            CvssScore: cached.CvssScore,
            CvssVector: cached.CvssVector,
            PublishedDate: cached.PublishedDate,
            References: references,
            Applicabilities: applicabilities);
    }
}

public record NvdBackfillStats(int Processed, int Succeeded, int Failed);
