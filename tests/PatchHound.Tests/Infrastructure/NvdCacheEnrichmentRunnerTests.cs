using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Tests.Infrastructure;

public class NvdCacheEnrichmentRunnerTests
{
    [Fact]
    public async Task ExecuteAsync_returns_Succeeded_and_writes_canonical_rows_from_cache()
    {
        await using var db = await TestDbContextFactory.CreateAsync();

        var vuln = Vulnerability.Create("nvd", "CVE-2024-9999", "placeholder",
            string.Empty, Severity.Medium, null, null, null);
        db.Vulnerabilities.Add(vuln);

        var refs = JsonSerializer.Serialize(new[]
        {
            new NvdCachedReference("https://nvd.nist.gov/vuln/detail/CVE-2024-9999", "NVD", ["Vendor Advisory"])
        });
        var configs = JsonSerializer.Serialize(new[]
        {
            new NvdCachedCpeMatch(true, "cpe:2.3:a:acme:widget:1.0:*:*:*:*:*:*:*:*", null, null, null, null)
        });
        db.NvdCveCache.Add(NvdCveCache.Create("CVE-2024-9999",
            "Cache-based description", 9.8m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:C/C:H/I:H/A:H",
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DateTimeOffset.UtcNow, refs, configs));
        await db.SaveChangesAsync();

        var scopeFactory = new FakeScopeFactory(db);
        var runner = new NvdVulnerabilityEnrichmentRunner(
            scopeFactory, NullLogger<NvdVulnerabilityEnrichmentRunner>.Instance);

        var job = EnrichmentJob.Create(Guid.NewGuid(), "nvd",
            EnrichmentTargetModel.Vulnerability, vuln.Id, vuln.ExternalId, 100,
            DateTimeOffset.UtcNow);

        var result = await runner.ExecuteAsync(job, CancellationToken.None);

        result.Outcome.Should().Be(EnrichmentJobExecutionOutcome.Succeeded);
        var reloaded = await db.Vulnerabilities.SingleAsync();
        reloaded.Description.Should().Be("Cache-based description");
        reloaded.CvssScore.Should().Be(9.8m);

        var dbRefs = await db.VulnerabilityReferences.ToListAsync();
        dbRefs.Should().ContainSingle(r =>
            r.Url == "https://nvd.nist.gov/vuln/detail/CVE-2024-9999");

        var dbApps = await db.VulnerabilityApplicabilities.ToListAsync();
        dbApps.Should().ContainSingle(a =>
            a.CpeCriteria == "cpe:2.3:a:acme:widget:1.0:*:*:*:*:*:*:*:*");
        dbApps.Single().SoftwareProductId.Should().BeNull("no alias exists for this CPE in the test data");
    }

    [Fact]
    public async Task ExecuteAsync_returns_NoData_when_cve_not_in_cache()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var vuln = Vulnerability.Create("nvd", "CVE-2024-0001", "placeholder",
            string.Empty, Severity.Medium, null, null, null);
        db.Vulnerabilities.Add(vuln);
        await db.SaveChangesAsync();

        var scopeFactory = new FakeScopeFactory(db);
        var runner = new NvdVulnerabilityEnrichmentRunner(
            scopeFactory, NullLogger<NvdVulnerabilityEnrichmentRunner>.Instance);

        var job = EnrichmentJob.Create(Guid.NewGuid(), "nvd",
            EnrichmentTargetModel.Vulnerability, vuln.Id, vuln.ExternalId, 100,
            DateTimeOffset.UtcNow);

        var result = await runner.ExecuteAsync(job, CancellationToken.None);

        result.Outcome.Should().Be(EnrichmentJobExecutionOutcome.NoData);
    }

    [Fact]
    public async Task ExecuteAsync_returns_NoData_when_target_vulnerability_missing()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var scopeFactory = new FakeScopeFactory(db);
        var runner = new NvdVulnerabilityEnrichmentRunner(
            scopeFactory, NullLogger<NvdVulnerabilityEnrichmentRunner>.Instance);

        var job = EnrichmentJob.Create(Guid.NewGuid(), "nvd",
            EnrichmentTargetModel.Vulnerability, Guid.NewGuid(), "CVE-2024-0000", 100,
            DateTimeOffset.UtcNow);

        var result = await runner.ExecuteAsync(job, CancellationToken.None);

        result.Outcome.Should().Be(EnrichmentJobExecutionOutcome.NoData);
    }

    [Fact]
    public async Task ExecuteAsync_resolves_applicability_to_SoftwareProduct_when_alias_exists()
    {
        await using var db = await TestDbContextFactory.CreateAsync();

        var nvdSource = SourceSystem.Create("nvd", "NVD");
        db.SourceSystems.Add(nvdSource);
        var product = SoftwareProduct.Create("Acme", "Widget",
            "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
        db.SoftwareProducts.Add(product);
        db.SoftwareAliases.Add(SoftwareAlias.Create(product.Id, nvdSource.Id,
            "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*"));

        var vuln = Vulnerability.Create("nvd", "CVE-2024-5555", "placeholder",
            string.Empty, Severity.Medium, null, null, null);
        db.Vulnerabilities.Add(vuln);

        var configs = JsonSerializer.Serialize(new[]
        {
            new NvdCachedCpeMatch(true, "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*",
                null, null, null, null)
        });
        db.NvdCveCache.Add(NvdCveCache.Create("CVE-2024-5555", "desc", 7m, null,
            null, DateTimeOffset.UtcNow, "[]", configs));
        await db.SaveChangesAsync();

        var scopeFactory = new FakeScopeFactory(db);
        var runner = new NvdVulnerabilityEnrichmentRunner(
            scopeFactory, NullLogger<NvdVulnerabilityEnrichmentRunner>.Instance);

        var job = EnrichmentJob.Create(Guid.NewGuid(), "nvd",
            EnrichmentTargetModel.Vulnerability, vuln.Id, vuln.ExternalId, 100,
            DateTimeOffset.UtcNow);

        var result = await runner.ExecuteAsync(job, CancellationToken.None);

        result.Outcome.Should().Be(EnrichmentJobExecutionOutcome.Succeeded);
        var app = await db.VulnerabilityApplicabilities.SingleAsync();
        app.SoftwareProductId.Should().Be(product.Id);
    }

    private sealed class FakeScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        private readonly PatchHoundDbContext _db;

        public FakeScopeFactory(PatchHoundDbContext db) => _db = db;

        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => this;
        public void Dispose() { }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(PatchHoundDbContext)) return _db;
            if (serviceType == typeof(VulnerabilityResolver)) return new VulnerabilityResolver(_db);
            return null;
        }
    }
}
