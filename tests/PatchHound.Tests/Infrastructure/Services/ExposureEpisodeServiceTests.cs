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

public class ExposureEpisodeServiceTests
{
    [Fact]
    public async Task Opens_episode_1_on_first_observation()
    {
        var tenantId = Guid.NewGuid();
        await using var db = await CreateTenantDbAsync(tenantId);
        var exposure = await SeedOpenExposureAsync(db, tenantId, DateTimeOffset.UtcNow);

        var svc = new ExposureEpisodeService(db);
        await svc.SyncEpisodesForTenantAsync(tenantId, DateTimeOffset.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();

        var episodes = await db.ExposureEpisodes.Where(e => e.DeviceVulnerabilityExposureId == exposure.Id).ToListAsync();
        episodes.Should().ContainSingle();
        episodes[0].EpisodeNumber.Should().Be(1);
        episodes[0].ClosedAt.Should().BeNull();
    }

    [Fact]
    public async Task Closes_open_episode_when_exposure_resolves()
    {
        var tenantId = Guid.NewGuid();
        await using var db = await CreateTenantDbAsync(tenantId);
        var openedAt = new DateTimeOffset(2026, 4, 14, 0, 0, 0, TimeSpan.Zero);
        var exposure = await SeedOpenExposureAsync(db, tenantId, openedAt);

        var svc = new ExposureEpisodeService(db);
        await svc.SyncEpisodesForTenantAsync(tenantId, openedAt, CancellationToken.None);
        await db.SaveChangesAsync();

        var resolvedAt = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);
        exposure.Resolve(resolvedAt);
        await db.SaveChangesAsync();

        await svc.SyncEpisodesForTenantAsync(tenantId, resolvedAt, CancellationToken.None);
        await db.SaveChangesAsync();

        var episodes = await db.ExposureEpisodes.ToListAsync();
        episodes.Should().ContainSingle();
        episodes[0].ClosedAt.Should().Be(resolvedAt);
    }

    [Fact]
    public async Task Opens_new_episode_when_exposure_reopens_after_resolve()
    {
        var tenantId = Guid.NewGuid();
        await using var db = await CreateTenantDbAsync(tenantId);
        var exposure = await SeedOpenExposureAsync(db, tenantId, new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));

        var svc = new ExposureEpisodeService(db);
        await svc.SyncEpisodesForTenantAsync(tenantId, new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);
        await db.SaveChangesAsync();

        exposure.Resolve(new DateTimeOffset(2026, 4, 5, 0, 0, 0, TimeSpan.Zero));
        await db.SaveChangesAsync();
        await svc.SyncEpisodesForTenantAsync(tenantId, new DateTimeOffset(2026, 4, 5, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);
        await db.SaveChangesAsync();

        exposure.Reobserve(new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero));
        await db.SaveChangesAsync();
        await svc.SyncEpisodesForTenantAsync(tenantId, new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);
        await db.SaveChangesAsync();

        var episodes = await db.ExposureEpisodes.OrderBy(e => e.EpisodeNumber).ToListAsync();
        episodes.Should().HaveCount(2);
        episodes[0].EpisodeNumber.Should().Be(1);
        episodes[0].ClosedAt.Should().NotBeNull();
        episodes[1].EpisodeNumber.Should().Be(2);
        episodes[1].ClosedAt.Should().BeNull();
    }

    private static async Task<DeviceVulnerabilityExposure> SeedOpenExposureAsync(PatchHoundDbContext db, Guid tenantId, DateTimeOffset observedAt)
    {
        var sourceSystem = SourceSystem.Create("test", "Test");
        var product = SoftwareProduct.Create("Acme", "Widget", "cpe:2.3:a:acme:widget:*:*:*:*:*:*:*:*");
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
            observedAt);
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
