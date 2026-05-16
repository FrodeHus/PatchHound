using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure.Services;

public class ExposureEpisodeServiceRunScopedTests
{
    [Fact]
    public async Task SyncEpisodesForTenantAsync_only_touches_exposures_from_current_run()
    {
        var tenantId = Guid.NewGuid();
        await using var db = await CreateTenantDbAsync(tenantId);
        var observedAt = DateTimeOffset.UtcNow;

        var runA = Guid.NewGuid();
        var runB = Guid.NewGuid();
        var exposureA = await SeedOpenExposureAsync(db, tenantId, observedAt, runA);
        var exposureB = await SeedOpenExposureAsync(db, tenantId, observedAt, runB);

        var svc = new ExposureEpisodeService(db);
        await svc.SyncEpisodesForTenantAsync(tenantId, runB, observedAt, CancellationToken.None);
        await db.SaveChangesAsync();

        var episodesForA = await db.ExposureEpisodes
            .Where(e => e.DeviceVulnerabilityExposureId == exposureA.Id)
            .ToListAsync();
        var episodesForB = await db.ExposureEpisodes
            .Where(e => e.DeviceVulnerabilityExposureId == exposureB.Id)
            .ToListAsync();

        episodesForA.Should().BeEmpty();
        episodesForB.Should().ContainSingle();
    }

    [Fact]
    public async Task SyncEpisodesForTenantAsync_closes_episode_for_exposures_resolved_this_run()
    {
        var tenantId = Guid.NewGuid();
        await using var db = await CreateTenantDbAsync(tenantId);
        var observedAt = new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero);

        var runA = Guid.NewGuid();
        var exposureA = await SeedOpenExposureAsync(db, tenantId, observedAt, runA);

        var svc = new ExposureEpisodeService(db);
        // Open the initial episode under runA.
        await svc.SyncEpisodesForTenantAsync(tenantId, runA, observedAt, CancellationToken.None);
        await db.SaveChangesAsync();

        // Now resolve the exposure as part of a later run. Resolve() deliberately does not
        // update LastSeenRunId, so the run-scoped filter would normally miss it; the
        // ResolvedAt-recency fallback should still find it.
        var now = observedAt.AddHours(2);
        var resolvedAt = observedAt.AddHours(1);
        exposureA.Resolve(resolvedAt);
        await db.SaveChangesAsync();

        var runB = Guid.NewGuid();
        await svc.SyncEpisodesForTenantAsync(tenantId, runB, now, CancellationToken.None);
        await db.SaveChangesAsync();

        var episodes = await db.ExposureEpisodes
            .Where(e => e.DeviceVulnerabilityExposureId == exposureA.Id)
            .ToListAsync();
        episodes.Should().ContainSingle();
        episodes[0].ClosedAt.Should().Be(resolvedAt);
    }

    [Fact]
    public async Task SyncEpisodesForTenantAsync_batches_new_episode_inserts()
    {
        var tenantId = Guid.NewGuid();
        await using var db = await CreateTenantDbAsync(tenantId);
        var observedAt = DateTimeOffset.UtcNow;
        var currentRun = Guid.NewGuid();

        var exposureIds = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var exposure = await SeedOpenExposureAsync(db, tenantId, observedAt, currentRun);
            exposureIds.Add(exposure.Id);
        }

        var svc = new ExposureEpisodeService(db);
        await svc.SyncEpisodesForTenantAsync(tenantId, currentRun, observedAt, CancellationToken.None);
        await db.SaveChangesAsync();

        var episodes = await db.ExposureEpisodes
            .Where(e => exposureIds.Contains(e.DeviceVulnerabilityExposureId))
            .ToListAsync();
        episodes.Should().HaveCount(5);
        episodes.Should().OnlyContain(e => e.EpisodeNumber == 1 && e.ClosedAt == null);
    }

    private static async Task<DeviceVulnerabilityExposure> SeedOpenExposureAsync(PatchHoundDbContext db, Guid tenantId, DateTimeOffset observedAt, Guid runId)
    {
        var unique = Guid.NewGuid().ToString("N");
        var sourceSystem = SourceSystem.Create($"t{unique}", "Test");
        var product = SoftwareProduct.Create("Acme", $"Widget-{unique}", $"cpe:2.3:a:acme:{unique}:*:*:*:*:*:*:*:*");
        var vulnerability = Vulnerability.Create("nvd", $"CVE-{Guid.NewGuid():N}", "t", "d", Severity.High, 7.5m, "v", observedAt);
        var device = Device.Create(tenantId, sourceSystem.Id, $"dev-{Guid.NewGuid():N}", "Device", Criticality.Medium);

        db.SourceSystems.Add(sourceSystem);
        db.SoftwareProducts.Add(product);
        db.Vulnerabilities.Add(vulnerability);
        db.Devices.Add(device);
        await db.SaveChangesAsync();

        var installed = InstalledSoftware.Observe(tenantId, device.Id, product.Id, sourceSystem.Id, "1.0", observedAt);
        db.InstalledSoftware.Add(installed);
        await db.SaveChangesAsync();

        var exposure = DeviceVulnerabilityExposure.Observe(
            tenantId,
            device.Id,
            vulnerability.Id,
            product.Id,
            installed.Id,
            installed.Version,
            ExposureMatchSource.Product,
            observedAt,
            runId: runId);
        db.DeviceVulnerabilityExposures.Add(exposure);
        await db.SaveChangesAsync();
        return exposure;
    }

    private static async Task<PatchHoundDbContext> CreateTenantDbAsync(Guid tenantId)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(tenantId);
        tenantContext.AccessibleTenantIds.Returns([tenantId]);
        tenantContext.IsSystemContext.Returns(false);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(tenantContext));
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}
