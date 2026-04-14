using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure;

namespace PatchHound.Tests.Infrastructure.Services;

/// <summary>
/// Phase-5 canonical tests for <see cref="RiskScoreService"/>.
/// Covers: device scores driven by EnvironmentalCvss, software scores keyed by SoftwareProductId,
/// and resolved exposures being excluded from software rollups.
/// </summary>
public class RiskScoreServiceCanonicalTests : IAsyncDisposable
{
    private readonly Guid _tenantId;
    private readonly global::PatchHound.Infrastructure.Data.PatchHoundDbContext _db;

    public RiskScoreServiceCanonicalTests()
    {
        _db = TestDbContextFactory.CreateTenantContext(out _tenantId);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    // ── Test 1 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task RecalculateForTenantAsync_DeviceScore_DrivenByEnvironmentalCvss()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);
        var svc = new RiskScoreService(_db, Substitute.For<ILogger<RiskScoreService>>());

        await svc.RecalculateForTenantAsync(_tenantId, CancellationToken.None);

        // AssessmentA has environmentalCvss = 9.5 (Critical), AssessmentB = 7.0 (High)
        var scoreA = _db.DeviceRiskScores.Single(s => s.DeviceId == seed.DeviceA.Id);
        var scoreB = _db.DeviceRiskScores.Single(s => s.DeviceId == seed.DeviceB.Id);

        scoreA.OverallScore.Should().BeGreaterThan(0m);
        scoreA.CriticalCount.Should().Be(1);
        scoreA.HighCount.Should().Be(0);

        scoreB.OverallScore.Should().BeGreaterThan(0m);
        scoreB.CriticalCount.Should().Be(0);
        scoreB.HighCount.Should().Be(1);
    }

    // ── Test 2 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task RecalculateForTenantAsync_SoftwareScore_KeyedBySoftwareProductId()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);
        var svc = new RiskScoreService(_db, Substitute.For<ILogger<RiskScoreService>>());

        await svc.RecalculateForTenantAsync(_tenantId, CancellationToken.None);

        var scores = _db.SoftwareRiskScores.ToList();
        scores.Should().HaveCount(2);
        scores.Should().ContainSingle(s => s.SoftwareProductId == seed.ProductA.Id);
        scores.Should().ContainSingle(s => s.SoftwareProductId == seed.ProductB.Id);

        var scoreA = scores.Single(s => s.SoftwareProductId == seed.ProductA.Id);
        scoreA.AffectedDeviceCount.Should().Be(1);
        scoreA.CriticalExposureCount.Should().Be(1);
        scoreA.OverallScore.Should().BeGreaterThan(0m);
    }

    // ── Test 3 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task RecalculateForTenantAsync_ResolvedExposure_ExcludedFromSoftwareScore()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);

        // Resolve ExposureA so it should NOT count toward ProductA's score
        seed.ExposureA.Resolve(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync();

        var svc = new RiskScoreService(_db, Substitute.For<ILogger<RiskScoreService>>());
        await svc.RecalculateForTenantAsync(_tenantId, CancellationToken.None);

        var scores = _db.SoftwareRiskScores.ToList();
        // ProductA has only the now-resolved exposure; it should have 0 open exposures
        // (or no score row at all, depending on implementation)
        var scoreA = scores.SingleOrDefault(s => s.SoftwareProductId == seed.ProductA.Id);
        if (scoreA is not null)
        {
            scoreA.OpenExposureCount.Should().Be(0);
            scoreA.CriticalExposureCount.Should().Be(0);
        }

        // ProductB still has an open exposure
        var scoreB = scores.Single(s => s.SoftwareProductId == seed.ProductB.Id);
        scoreB.OpenExposureCount.Should().BeGreaterThan(0);
    }
}
