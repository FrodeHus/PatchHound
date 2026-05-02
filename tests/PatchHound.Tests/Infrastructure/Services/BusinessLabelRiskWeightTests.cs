using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure;

namespace PatchHound.Tests.Infrastructure.Services;

/// <summary>
/// Tests that verify business label weights are applied to asset risk scores
/// during RecalculateForTenantAsync.
/// </summary>
public class BusinessLabelRiskWeightTests : IAsyncDisposable
{
    private readonly Guid _tenantId;
    private readonly global::PatchHound.Infrastructure.Data.PatchHoundDbContext _db;
    private readonly RiskScoreService _svc;

    public BusinessLabelRiskWeightTests()
    {
        _db = TestDbContextFactory.CreateTenantContext(out _tenantId);
        _svc = new RiskScoreService(_db, Substitute.For<ILogger<RiskScoreService>>());
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    // ── Highest active label weight wins ─────────────────────────────────────

    [Fact]
    public async Task RecalculateForTenantAsync_AppliesHighestActiveLabelWeight()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);

        // Baseline: no labels on DeviceA
        await _svc.RecalculateForTenantAsync(_tenantId, CancellationToken.None);
        var baselineScore = _db.DeviceRiskScores.Single(s => s.DeviceId == seed.DeviceA.Id).OverallScore;

        // Assign a Normal (1.0x) and a Sensitive (1.5x) label — highest wins
        var normalLabel = BusinessLabel.Create(_tenantId, "Normal", null, null, BusinessLabelWeightCategory.Normal);
        var sensitiveLabel = BusinessLabel.Create(_tenantId, "Sensitive", null, null, BusinessLabelWeightCategory.Sensitive);
        await _db.AddRangeAsync(normalLabel, sensitiveLabel);
        _db.DeviceBusinessLabels.Add(DeviceBusinessLabel.Create(_tenantId, seed.DeviceA.Id, normalLabel.Id));
        _db.DeviceBusinessLabels.Add(DeviceBusinessLabel.Create(_tenantId, seed.DeviceA.Id, sensitiveLabel.Id));
        _db.DeviceRiskScores.RemoveRange(_db.DeviceRiskScores);
        await _db.SaveChangesAsync();

        var svc2 = new RiskScoreService(_db, Substitute.For<ILogger<RiskScoreService>>());
        await svc2.RecalculateForTenantAsync(_tenantId, CancellationToken.None);

        var weightedScore = _db.DeviceRiskScores.Single(s => s.DeviceId == seed.DeviceA.Id).OverallScore;

        // Sensitive (1.5x) should have been applied — score must be higher than baseline
        weightedScore.Should().BeGreaterThan(baselineScore);
    }

    [Fact]
    public async Task RecalculateForTenantAsync_CriticalLabelDoublesBaseScore()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);

        // Record baseline score without any label
        await _svc.RecalculateForTenantAsync(_tenantId, CancellationToken.None);
        var baselineScore = _db.DeviceRiskScores.Single(s => s.DeviceId == seed.DeviceA.Id).OverallScore;
        _db.DeviceRiskScores.RemoveRange(_db.DeviceRiskScores);
        await _db.SaveChangesAsync();

        // Assign a Critical (2.0x) label
        var criticalLabel = BusinessLabel.Create(_tenantId, "Critical", null, null, BusinessLabelWeightCategory.Critical);
        await _db.AddAsync(criticalLabel);
        await _db.SaveChangesAsync();
        _db.DeviceBusinessLabels.Add(DeviceBusinessLabel.Create(_tenantId, seed.DeviceA.Id, criticalLabel.Id));
        await _db.SaveChangesAsync();

        var svc2 = new RiskScoreService(_db, Substitute.For<ILogger<RiskScoreService>>());
        await svc2.RecalculateForTenantAsync(_tenantId, CancellationToken.None);

        var weightedScore = _db.DeviceRiskScores.Single(s => s.DeviceId == seed.DeviceA.Id).OverallScore;

        // 2x multiplier — clamped at 1000
        var expectedMax = Math.Clamp(Math.Round(baselineScore * 2.0m, 2), 0m, 1000m);
        weightedScore.Should().Be(expectedMax);
    }

    [Fact]
    public async Task RecalculateForTenantAsync_InformationalLabelReducesScore()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);

        await _svc.RecalculateForTenantAsync(_tenantId, CancellationToken.None);
        var baselineScore = _db.DeviceRiskScores.Single(s => s.DeviceId == seed.DeviceA.Id).OverallScore;
        _db.DeviceRiskScores.RemoveRange(_db.DeviceRiskScores);
        await _db.SaveChangesAsync();

        var infoLabel = BusinessLabel.Create(_tenantId, "Info", null, null, BusinessLabelWeightCategory.Informational);
        await _db.AddAsync(infoLabel);
        await _db.SaveChangesAsync();
        _db.DeviceBusinessLabels.Add(DeviceBusinessLabel.Create(_tenantId, seed.DeviceA.Id, infoLabel.Id));
        await _db.SaveChangesAsync();

        var svc2 = new RiskScoreService(_db, Substitute.For<ILogger<RiskScoreService>>());
        await svc2.RecalculateForTenantAsync(_tenantId, CancellationToken.None);

        var weightedScore = _db.DeviceRiskScores.Single(s => s.DeviceId == seed.DeviceA.Id).OverallScore;
        var expected = Math.Clamp(Math.Round(baselineScore * 0.5m, 2), 0m, 1000m);
        weightedScore.Should().Be(expected);
    }

    [Fact]
    public async Task RecalculateForTenantAsync_InactiveLabelNotApplied()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);

        // Baseline
        await _svc.RecalculateForTenantAsync(_tenantId, CancellationToken.None);
        var baselineScore = _db.DeviceRiskScores.Single(s => s.DeviceId == seed.DeviceA.Id).OverallScore;
        _db.DeviceRiskScores.RemoveRange(_db.DeviceRiskScores);
        await _db.SaveChangesAsync();

        // Create a Critical label but mark it inactive
        var criticalLabel = BusinessLabel.Create(_tenantId, "Critical-Inactive", null, null, BusinessLabelWeightCategory.Critical);
        criticalLabel.Update(criticalLabel.Name, criticalLabel.Description, criticalLabel.Color, isActive: false, BusinessLabelWeightCategory.Critical);
        await _db.AddAsync(criticalLabel);
        await _db.SaveChangesAsync();
        _db.DeviceBusinessLabels.Add(DeviceBusinessLabel.Create(_tenantId, seed.DeviceA.Id, criticalLabel.Id));
        await _db.SaveChangesAsync();

        var svc2 = new RiskScoreService(_db, Substitute.For<ILogger<RiskScoreService>>());
        await svc2.RecalculateForTenantAsync(_tenantId, CancellationToken.None);

        var score = _db.DeviceRiskScores.Single(s => s.DeviceId == seed.DeviceA.Id).OverallScore;
        score.Should().Be(baselineScore);
    }

    [Fact]
    public async Task RecalculateForTenantAsync_ScoreClampsAt1000()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);

        var criticalLabel = BusinessLabel.Create(_tenantId, "Critical", null, null, BusinessLabelWeightCategory.Critical);
        await _db.AddAsync(criticalLabel);
        await _db.SaveChangesAsync();
        _db.DeviceBusinessLabels.Add(DeviceBusinessLabel.Create(_tenantId, seed.DeviceA.Id, criticalLabel.Id));
        await _db.SaveChangesAsync();

        await _svc.RecalculateForTenantAsync(_tenantId, CancellationToken.None);

        var score = _db.DeviceRiskScores.Single(s => s.DeviceId == seed.DeviceA.Id).OverallScore;
        score.Should().BeLessThanOrEqualTo(1000m);
        score.Should().BeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public async Task RecalculateForTenantAsync_NormalLabelLeavesScoreUnchanged()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);

        await _svc.RecalculateForTenantAsync(_tenantId, CancellationToken.None);
        var baselineScore = _db.DeviceRiskScores.Single(s => s.DeviceId == seed.DeviceA.Id).OverallScore;
        _db.DeviceRiskScores.RemoveRange(_db.DeviceRiskScores);
        await _db.SaveChangesAsync();

        var normalLabel = BusinessLabel.Create(_tenantId, "Normal", null, null, BusinessLabelWeightCategory.Normal);
        await _db.AddAsync(normalLabel);
        await _db.SaveChangesAsync();
        _db.DeviceBusinessLabels.Add(DeviceBusinessLabel.Create(_tenantId, seed.DeviceA.Id, normalLabel.Id));
        await _db.SaveChangesAsync();

        var svc2 = new RiskScoreService(_db, Substitute.For<ILogger<RiskScoreService>>());
        await svc2.RecalculateForTenantAsync(_tenantId, CancellationToken.None);

        var score = _db.DeviceRiskScores.Single(s => s.DeviceId == seed.DeviceA.Id).OverallScore;
        score.Should().Be(baselineScore);
    }
}
