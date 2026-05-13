using FluentAssertions;
using NSubstitute;
using PatchHound.Api.Services;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Tests.Infrastructure;

namespace PatchHound.Tests.Api;

/// <summary>
/// Phase-5 canonical tests for <see cref="DashboardQueryService"/>.
/// Covers: recurrence detection from ExposureEpisodes.EpisodeNumber &gt; 1,
/// risk-change-brief appeared/resolved windowing, and RemediationCaseId wiring.
/// </summary>
public class DashboardQueryServiceCanonicalTests : IAsyncDisposable
{
    private readonly Guid _tenantId;
    private readonly global::PatchHound.Infrastructure.Data.PatchHoundDbContext _db;

    public DashboardQueryServiceCanonicalTests()
    {
        _db = TestDbContextFactory.CreateTenantContext(out _tenantId);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private DashboardQueryService CreateSut() =>
        new(_db, Substitute.For<IRiskChangeBriefAiSummaryService>());

    // ── Test 1 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetRecurrenceDataAsync_SingleEpisode_NotCountedAsRecurring()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);

        // Episode 1 = first occurrence, should NOT be recurring
        var episode = ExposureEpisode.Open(_tenantId, seed.ExposureA.Id, 1, DateTimeOffset.UtcNow);
        _db.ExposureEpisodes.Add(episode);
        await _db.SaveChangesAsync();

        var svc = CreateSut();
        var result = await svc.GetRecurrenceDataAsync(_tenantId, CancellationToken.None);

        result.RecurringVulnerabilityCount.Should().Be(0);
        result.RecurrenceRatePercent.Should().Be(0m);
        result.TopRecurringVulnerabilities.Should().BeEmpty();
    }

    // ── Test 2 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetRecurrenceDataAsync_EpisodeNumberGreaterThanOne_CountedAsRecurring()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);

        // Episode 2 = reappearance, should count as recurring
        var episode = ExposureEpisode.Open(_tenantId, seed.ExposureA.Id, 2, DateTimeOffset.UtcNow);
        _db.ExposureEpisodes.Add(episode);
        await _db.SaveChangesAsync();

        var svc = CreateSut();
        var result = await svc.GetRecurrenceDataAsync(_tenantId, CancellationToken.None);

        result.RecurringVulnerabilityCount.Should().Be(1);
        result.TopRecurringVulnerabilities.Should().HaveCount(1);
        result.TopRecurringAssets.Should().HaveCount(1);
        result.TopRecurringAssets[0].AssetId.Should().Be(seed.DeviceA.Id);
    }

    [Fact]
    public async Task GetRecurrenceDataAsync_AcceptedRiskVulnerability_NotListedOrCounted()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);
        var episode = ExposureEpisode.Open(_tenantId, seed.ExposureA.Id, 2, DateTimeOffset.UtcNow);
        _db.ExposureEpisodes.Add(episode);
        await AddApprovedRemediationAsync(seed.ProductA.Id, seed.ExposureA.VulnerabilityId, RemediationOutcome.RiskAcceptance);

        var svc = CreateSut();
        var result = await svc.GetRecurrenceDataAsync(_tenantId, CancellationToken.None);

        result.RecurringVulnerabilityCount.Should().Be(0);
        result.RecurrenceRatePercent.Should().Be(0m);
        result.TopRecurringVulnerabilities.Should().BeEmpty();
        result.TopRecurringAssets.Should().BeEmpty();
    }

    // ── Test 3 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task BuildRiskChangeBriefAsync_AppearedExposureWithinCutoff_IsInBrief()
    {
        // ExposureA was seeded with FirstObservedAt = UtcNow, within 24h window
        await CanonicalSeed.PlantAsync(_db, _tenantId);

        var svc = CreateSut();
        var result = await svc.BuildRiskChangeBriefAsync(
            _tenantId, _tenantId, limit: null, highCriticalOnly: false,
            CancellationToken.None, cutoffHours: 24);

        result.AppearedCount.Should().BeGreaterThan(0);
        result.Appeared.Should().NotBeEmpty();
    }

    [Fact]
    public async Task BuildRiskChangeBriefAsync_AcceptedRiskAppearedExposure_NotListedOrCounted()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);
        await AddApprovedRemediationAsync(seed.ProductA.Id, seed.ExposureA.VulnerabilityId, RemediationOutcome.RiskAcceptance);

        var svc = CreateSut();
        var result = await svc.BuildRiskChangeBriefAsync(
            _tenantId, _tenantId, limit: null, highCriticalOnly: false,
            CancellationToken.None, cutoffHours: 24);

        result.AppearedCount.Should().Be(1);
        result.Appeared.Should().NotContain(item => item.VulnerabilityId == seed.ExposureA.VulnerabilityId);
    }

    // ── Test 4 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task BuildRiskChangeBriefAsync_ResolvedEpisodeWithinCutoff_IsInBrief()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);

        var episode = ExposureEpisode.Open(_tenantId, seed.ExposureA.Id, 1, DateTimeOffset.UtcNow.AddHours(-1));
        episode.Close(DateTimeOffset.UtcNow);
        _db.ExposureEpisodes.Add(episode);
        await _db.SaveChangesAsync();

        var svc = CreateSut();
        var result = await svc.BuildRiskChangeBriefAsync(
            _tenantId, _tenantId, limit: null, highCriticalOnly: false,
            CancellationToken.None, cutoffHours: 24);

        result.ResolvedCount.Should().BeGreaterThan(0);
        result.Resolved.Should().NotBeEmpty();
        result.Resolved.Single(item => item.VulnerabilityId == seed.ExposureA.VulnerabilityId)
            .AffectedAssetCount.Should().Be(1);
    }

    [Fact]
    public async Task BuildRiskChangeBriefAsync_ResolvedItem_CountsAssetsFromClosedExposureEpisodes()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);
        var now = DateTimeOffset.UtcNow;

        var secondExposure = DeviceVulnerabilityExposure.Observe(
            _tenantId,
            seed.DeviceB.Id,
            seed.ExposureA.VulnerabilityId,
            seed.ProductA.Id,
            null,
            "1.0.0",
            ExposureMatchSource.Product,
            now.AddHours(-2));
        _db.DeviceVulnerabilityExposures.Add(secondExposure);
        await _db.SaveChangesAsync();

        var firstEpisode = ExposureEpisode.Open(_tenantId, seed.ExposureA.Id, 1, now.AddHours(-2));
        var secondEpisode = ExposureEpisode.Open(_tenantId, secondExposure.Id, 1, now.AddHours(-2));
        firstEpisode.Close(now);
        secondEpisode.Close(now);
        _db.ExposureEpisodes.AddRange(firstEpisode, secondEpisode);
        await _db.SaveChangesAsync();

        var svc = CreateSut();
        var result = await svc.BuildRiskChangeBriefAsync(
            _tenantId, _tenantId, limit: null, highCriticalOnly: false,
            CancellationToken.None, cutoffHours: 24);

        result.Resolved.Single(item => item.VulnerabilityId == seed.ExposureA.VulnerabilityId)
            .AffectedAssetCount.Should().Be(2);
    }

    // ── Test 5 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task BuildRiskChangeBriefAsync_ExposureOutsideCutoff_NotInBrief()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);

        // Backdate the first-observed to 48 hours ago (outside 24h window)
        // We can't set private properties directly — instead use a very short cutoff window
        var svc = CreateSut();
        var result = await svc.BuildRiskChangeBriefAsync(
            _tenantId, _tenantId, limit: null, highCriticalOnly: false,
            CancellationToken.None, cutoffHours: 0); // 0-hour window: nothing qualifies

        result.AppearedCount.Should().Be(0);
        result.Appeared.Should().BeEmpty();
    }

    // ── Test 6 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task BuildRiskChangeBriefAsync_AppearedItem_CarriesRemediationCaseId()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);

        // Create a RemediationCase for ProductA
        var remCase = RemediationCase.Create(_tenantId, seed.ProductA.Id);
        _db.RemediationCases.Add(remCase);
        await _db.SaveChangesAsync();

        var svc = CreateSut();
        var result = await svc.BuildRiskChangeBriefAsync(
            _tenantId, _tenantId, limit: null, highCriticalOnly: false,
            CancellationToken.None, cutoffHours: 24);

        // ExposureA is linked to ProductA — its appeared item should carry the case id
        var itemForProductA = result.Appeared.FirstOrDefault(item =>
            item.RemediationCaseId == remCase.Id);
        itemForProductA.Should().NotBeNull("the appeared item for ProductA should carry the remediation case id");
    }

    // ── Test 7 ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task BuildRiskChangeBriefAsync_NoRemediationCase_RemediationCaseIdIsNull()
    {
        // Seed without creating any RemediationCase
        await CanonicalSeed.PlantAsync(_db, _tenantId);

        var svc = CreateSut();
        var result = await svc.BuildRiskChangeBriefAsync(
            _tenantId, _tenantId, limit: null, highCriticalOnly: false,
            CancellationToken.None, cutoffHours: 24);

        result.Appeared.Should().AllSatisfy(item =>
            item.RemediationCaseId.Should().BeNull());
    }

    private async Task AddApprovedRemediationAsync(
        Guid softwareProductId,
        Guid vulnerabilityId,
        RemediationOutcome outcome)
    {
        var remediationCase = RemediationCase.Create(_tenantId, softwareProductId);
        var decision = RemediationDecision.Create(
            _tenantId,
            remediationCase.Id,
            outcome,
            "Approved remediation",
            Guid.NewGuid(),
            DecisionApprovalStatus.Approved);

        _db.RemediationCases.Add(remediationCase);
        _db.RemediationDecisions.Add(decision);
        await _db.SaveChangesAsync();

        _db.ApprovedVulnerabilityRemediations.Add(ApprovedVulnerabilityRemediation.Create(
            _tenantId,
            vulnerabilityId,
            remediationCase.Id,
            decision.Id,
            decision.Outcome,
            decision.ApprovedAt!.Value));
        await _db.SaveChangesAsync();
    }
}
