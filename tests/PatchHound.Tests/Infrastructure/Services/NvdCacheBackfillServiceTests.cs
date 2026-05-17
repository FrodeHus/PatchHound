using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure.Services;

public class NvdCacheBackfillServiceTests
{
    [Fact]
    public async Task RunAsync_enriches_cve_missing_description_from_cache()
    {
        await using var db = await TestDbContextFactory.CreateAsync();

        var vuln = Vulnerability.Create("nvd", "CVE-2025-1111", "placeholder",
            string.Empty, Severity.Medium, null, null, null);
        db.Vulnerabilities.Add(vuln);

        var refs = JsonSerializer.Serialize(new[]
        {
            new NvdCachedReference("https://nvd.nist.gov/vuln/detail/CVE-2025-1111", "NVD", new List<string>())
        });
        var configs = JsonSerializer.Serialize(new[]
        {
            new NvdCachedCpeMatch(true, "cpe:2.3:a:acme:widget:1.0:*:*:*:*:*:*:*", null, null, null, null)
        });
        db.NvdCveCache.Add(NvdCveCache.Create("CVE-2025-1111",
            "Backfill description", 7.5m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:N/A:N",
            new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero),
            DateTimeOffset.UtcNow, refs, configs));
        await db.SaveChangesAsync();

        var resolver = new VulnerabilityResolver(db, NullLogger<VulnerabilityResolver>.Instance);
        var svc = new NvdCacheBackfillService(db, resolver, new InMemoryBulkVulnerabilityReferenceWriter(db), NullLogger<NvdCacheBackfillService>.Instance);

        var stats = await svc.RunAsync();

        stats.Processed.Should().Be(1);
        stats.Succeeded.Should().Be(1);
        stats.Failed.Should().Be(0);

        var reloaded = await db.Vulnerabilities.SingleAsync();
        reloaded.Description.Should().Be("Backfill description");
        reloaded.CvssScore.Should().Be(7.5m);
        reloaded.PublishedDate.Should().NotBeNull();

        var dbRefs = await db.VulnerabilityReferences.ToListAsync();
        dbRefs.Should().ContainSingle();

        var dbApps = await db.VulnerabilityApplicabilities.ToListAsync();
        dbApps.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_skips_cve_with_no_cache_entry()
    {
        await using var db = await TestDbContextFactory.CreateAsync();

        var vuln = Vulnerability.Create("nvd", "CVE-2025-2222", "placeholder",
            string.Empty, Severity.Low, null, null, null);
        db.Vulnerabilities.Add(vuln);
        await db.SaveChangesAsync();

        var resolver = new VulnerabilityResolver(db, NullLogger<VulnerabilityResolver>.Instance);
        var svc = new NvdCacheBackfillService(db, resolver, new InMemoryBulkVulnerabilityReferenceWriter(db), NullLogger<NvdCacheBackfillService>.Instance);

        var stats = await svc.RunAsync();

        stats.Processed.Should().Be(0);
        stats.Succeeded.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_skips_already_fully_enriched_cve()
    {
        await using var db = await TestDbContextFactory.CreateAsync();

        var vuln = Vulnerability.Create("nvd", "CVE-2025-3333", "Full description",
            "Full description", Severity.High, 9.0m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:C/C:H/I:H/A:H",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        db.Vulnerabilities.Add(vuln);
        db.VulnerabilityReferences.Add(
            VulnerabilityReference.Create(vuln.Id, "https://example.com/cve", "NVD", []));
        db.VulnerabilityApplicabilities.Add(
            VulnerabilityApplicability.Create(vuln.Id, null, "cpe:2.3:a:x:y:1.0:*", true,
                null, null, null, null));
        db.NvdCveCache.Add(NvdCveCache.Create("CVE-2025-3333",
            "Full description", 9.0m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:C/C:H/I:H/A:H",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DateTimeOffset.UtcNow, "[]", "[]"));
        await db.SaveChangesAsync();

        var resolver = new VulnerabilityResolver(db, NullLogger<VulnerabilityResolver>.Instance);
        var svc = new NvdCacheBackfillService(db, resolver, new InMemoryBulkVulnerabilityReferenceWriter(db), NullLogger<NvdCacheBackfillService>.Instance);

        var stats = await svc.RunAsync();

        stats.Processed.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_skips_non_cve_vulnerabilities()
    {
        await using var db = await TestDbContextFactory.CreateAsync();

        var vuln = Vulnerability.Create("vendor", "GHSA-xxxx-yyyy-zzzz", "placeholder",
            string.Empty, Severity.Medium, null, null, null);
        db.Vulnerabilities.Add(vuln);
        await db.SaveChangesAsync();

        var resolver = new VulnerabilityResolver(db, NullLogger<VulnerabilityResolver>.Instance);
        var svc = new NvdCacheBackfillService(db, resolver, new InMemoryBulkVulnerabilityReferenceWriter(db), NullLogger<NvdCacheBackfillService>.Instance);

        var stats = await svc.RunAsync();

        stats.Processed.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_resolves_cpe_to_software_product_via_alias_map()
    {
        await using var db = await TestDbContextFactory.CreateAsync();

        var nvdSource = SourceSystem.Create("nvd", "NVD");
        db.SourceSystems.Add(nvdSource);
        var product = SoftwareProduct.Create("Acme", "Widget",
            "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        db.SoftwareProducts.Add(product);
        db.SoftwareAliases.Add(SoftwareAlias.Create(product.Id, nvdSource.Id,
            "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*"));

        var vuln = Vulnerability.Create("nvd", "CVE-2025-4444", "placeholder",
            string.Empty, Severity.Medium, null, null, null);
        db.Vulnerabilities.Add(vuln);

        var configs = JsonSerializer.Serialize(new[]
        {
            new NvdCachedCpeMatch(true, "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*",
                null, null, null, null)
        });
        db.NvdCveCache.Add(NvdCveCache.Create("CVE-2025-4444", "desc", 7m, null,
            null, DateTimeOffset.UtcNow, "[]", configs));
        await db.SaveChangesAsync();

        var resolver = new VulnerabilityResolver(db, NullLogger<VulnerabilityResolver>.Instance);
        var svc = new NvdCacheBackfillService(db, resolver, new InMemoryBulkVulnerabilityReferenceWriter(db), NullLogger<NvdCacheBackfillService>.Instance);

        var stats = await svc.RunAsync();

        stats.Succeeded.Should().Be(1);
        var app = await db.VulnerabilityApplicabilities.SingleAsync();
        app.SoftwareProductId.Should().Be(product.Id);
    }

    [Fact]
    public async Task RunAsync_continues_after_individual_failure()
    {
        await using var db = await TestDbContextFactory.CreateAsync();

        var vuln1 = Vulnerability.Create("nvd", "CVE-2025-5550", "placeholder",
            string.Empty, Severity.Medium, null, null, null);
        var vuln2 = Vulnerability.Create("nvd", "CVE-2025-5551", "placeholder",
            string.Empty, Severity.Medium, null, null, null);
        db.Vulnerabilities.AddRange(vuln1, vuln2);

        db.NvdCveCache.Add(NvdCveCache.Create("CVE-2025-5550", "desc 1", 5m, null,
            null, DateTimeOffset.UtcNow, "[]", "[]"));
        db.NvdCveCache.Add(NvdCveCache.Create("CVE-2025-5551", "desc 2", 5m, null,
            null, DateTimeOffset.UtcNow, "[]", "[]"));
        await db.SaveChangesAsync();

        var resolver = new ThrowOnFirstCallResolver(db);
        var svc = new NvdCacheBackfillService(db, resolver, new InMemoryBulkVulnerabilityReferenceWriter(db), NullLogger<NvdCacheBackfillService>.Instance);

        var stats = await svc.RunAsync();

        stats.Processed.Should().Be(2);
        stats.Succeeded.Should().Be(1);
        stats.Failed.Should().Be(1);
    }

    private sealed class ThrowOnFirstCallResolver(PatchHoundDbContext db)
        : VulnerabilityResolver(db, NullLogger<VulnerabilityResolver>.Instance)
    {
        private bool _hasThrown;

        public override async Task<Vulnerability> ResolveAsync(
            VulnerabilityResolveInput input, CancellationToken ct)
        {
            if (!_hasThrown)
            {
                _hasThrown = true;
                throw new InvalidOperationException("Simulated per-CVE failure");
            }
            return await base.ResolveAsync(input, ct);
        }
    }

    [Fact]
    public async Task RunAsync_respects_batch_size()
    {
        await using var db = await TestDbContextFactory.CreateAsync();

        for (var i = 1; i <= 5; i++)
        {
            var cveId = $"CVE-2025-{9000 + i}";
            var vuln = Vulnerability.Create("nvd", cveId, "placeholder",
                string.Empty, Severity.Low, null, null, null);
            db.Vulnerabilities.Add(vuln);
            db.NvdCveCache.Add(NvdCveCache.Create(cveId, $"desc {i}", 3m, null,
                null, DateTimeOffset.UtcNow, "[]", "[]"));
        }
        await db.SaveChangesAsync();

        var resolver = new VulnerabilityResolver(db, NullLogger<VulnerabilityResolver>.Instance);
        var svc = new NvdCacheBackfillService(db, resolver, new InMemoryBulkVulnerabilityReferenceWriter(db), NullLogger<NvdCacheBackfillService>.Instance);

        var stats = await svc.RunAsync(batchSize: 3);

        stats.Processed.Should().Be(3);
        stats.Succeeded.Should().Be(3);
    }
}
