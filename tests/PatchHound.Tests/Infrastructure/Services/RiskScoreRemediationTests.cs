using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Tests.Infrastructure.Services;

/// <summary>
/// Tests that remediation decisions apply the correct score adjustments.
///
/// Decision rules:
///   RiskAcceptance      → no score change (visibility only)
///   AlternateMitigation → excluded from risk and impact while Approved
///   ApprovedForPatching → score lowered while Approved AND maintenance window not missed
///   ApprovedForPatching → score unchanged when maintenance window date is in the past
///   PatchingDeferred    → no score change (administrative only)
/// </summary>
public class RiskScoreRemediationTests : IAsyncDisposable
{
    private readonly Guid _tenantId;
    private readonly global::PatchHound.Infrastructure.Data.PatchHoundDbContext _db;

    public RiskScoreRemediationTests()
    {
        _db = TestDbContextFactory.CreateTenantContext(out _tenantId);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    private async Task<decimal?> GetDeviceScoreAsync(Guid deviceId)
    {
        await new RiskScoreService(_db, Substitute.For<ILogger<RiskScoreService>>())
            .RecalculateForTenantAsync(_tenantId, CancellationToken.None);

        return _db.DeviceRiskScores
            .SingleOrDefault(s => s.DeviceId == deviceId)
            ?.OverallScore;
    }

    private async Task<decimal> GetBaselineScoreAsync(Guid deviceId)
        => await GetDeviceScoreAsync(deviceId) ?? throw new InvalidOperationException("Expected baseline score.");

    /// <summary>
    /// Seeds a RemediationCase + RemediationDecision for <paramref name="softwareProductId"/>
    /// with the given outcome and approval status (default: Approved).
    /// Returns the decision so callers can tweak it before saving.
    /// </summary>
    private async Task<RemediationDecision> SeedDecisionAsync(
        Guid softwareProductId,
        RemediationOutcome outcome,
        DecisionApprovalStatus? approvalStatus = DecisionApprovalStatus.Approved,
        DateTimeOffset? maintenanceWindowDate = null)
    {
        var remCase = RemediationCase.Create(_tenantId, softwareProductId);
        _db.RemediationCases.Add(remCase);
        await _db.SaveChangesAsync();

        // ApprovedForPatching has no justification requirement; all others do.
        var justification = outcome == RemediationOutcome.ApprovedForPatching
            ? null
            : "test justification";

        // PatchingDeferred requires a re-evaluation date.
        DateTimeOffset? reEvaluationDate = outcome == RemediationOutcome.PatchingDeferred
            ? DateTimeOffset.UtcNow.AddDays(90)
            : null;

        var decision = RemediationDecision.Create(
            tenantId: _tenantId,
            remediationCaseId: remCase.Id,
            outcome: outcome,
            justification: justification,
            decidedBy: Guid.NewGuid(),
            initialApprovalStatus: approvalStatus,
            reEvaluationDate: reEvaluationDate,
            maintenanceWindowDate: maintenanceWindowDate
        );
        _db.RemediationDecisions.Add(decision);
        await _db.SaveChangesAsync();

        return decision;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 1 — RiskAcceptance → no score change
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecalculateForTenantAsync_RiskAcceptance_ScoreUnchanged()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);

        // Capture baseline before adding any decision
        var baseline = await GetBaselineScoreAsync(seed.DeviceA.Id);

        // Reset scores so the next call recalculates from scratch
        _db.DeviceRiskScores.RemoveRange(_db.DeviceRiskScores);
        await _db.SaveChangesAsync();

        await SeedDecisionAsync(
            seed.ProductA.Id,
            RemediationOutcome.RiskAcceptance,
            approvalStatus: DecisionApprovalStatus.Approved);

        var scoreAfter = await GetDeviceScoreAsync(seed.DeviceA.Id);

        scoreAfter.Should().Be(baseline,
            "RiskAcceptance provides visibility only and must not change the score");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 2 — AlternateMitigation (Approved) → exposure considered fixed
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecalculateForTenantAsync_AlternateMitigation_Approved_RemovesExposureFromRiskScores()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);

        _db.DeviceRiskScores.RemoveRange(_db.DeviceRiskScores);
        await _db.SaveChangesAsync();

        var decision = await SeedDecisionAsync(
            seed.ProductA.Id,
            RemediationOutcome.AlternateMitigation,
            approvalStatus: DecisionApprovalStatus.Approved);
        _db.ApprovedVulnerabilityRemediations.Add(ApprovedVulnerabilityRemediation.Create(
            _tenantId,
            seed.ExposureA.VulnerabilityId,
            decision.RemediationCaseId,
            decision.Id,
            decision.Outcome,
            decision.ApprovedAt!.Value));
        await _db.SaveChangesAsync();

        var scoreAfter = await GetDeviceScoreAsync(seed.DeviceA.Id);

        scoreAfter.Should().BeNull(
            "AlternateMitigation (Approved) should be considered fixed and removed from asset risk scoring");
        _db.SoftwareRiskScores.Should().NotContain(score => score.SoftwareProductId == seed.ProductA.Id);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 3 — ApprovedForPatching, window in the future → score lower
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecalculateForTenantAsync_ApprovedForPatching_WindowNotMissed_ScoreLowered()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);

        var baseline = await GetBaselineScoreAsync(seed.DeviceA.Id);

        _db.DeviceRiskScores.RemoveRange(_db.DeviceRiskScores);
        await _db.SaveChangesAsync();

        await SeedDecisionAsync(
            seed.ProductA.Id,
            RemediationOutcome.ApprovedForPatching,
            approvalStatus: DecisionApprovalStatus.Approved,
            maintenanceWindowDate: DateTimeOffset.UtcNow.AddDays(7));  // future window

        var scoreAfter = await GetDeviceScoreAsync(seed.DeviceA.Id);

        scoreAfter.Should().BeLessThan(baseline,
            "ApprovedForPatching with a future maintenance window must reduce the score");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 4 — ApprovedForPatching, window in the past (missed) → score = baseline
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecalculateForTenantAsync_ApprovedForPatching_WindowMissed_ScoreEqualsBaseline()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);

        var baseline = await GetBaselineScoreAsync(seed.DeviceA.Id);

        _db.DeviceRiskScores.RemoveRange(_db.DeviceRiskScores);
        await _db.SaveChangesAsync();

        await SeedDecisionAsync(
            seed.ProductA.Id,
            RemediationOutcome.ApprovedForPatching,
            approvalStatus: DecisionApprovalStatus.Approved,
            maintenanceWindowDate: DateTimeOffset.UtcNow.AddDays(-1));  // past = missed

        var scoreAfter = await GetDeviceScoreAsync(seed.DeviceA.Id);

        scoreAfter.Should().Be(baseline,
            "A missed maintenance window cancels the ApprovedForPatching reduction; score must return to baseline");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 5 — PatchingDeferred → no score change
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecalculateForTenantAsync_PatchingDeferred_ScoreUnchanged()
    {
        var seed = await CanonicalSeed.PlantAsync(_db, _tenantId);

        var baseline = await GetBaselineScoreAsync(seed.DeviceA.Id);

        _db.DeviceRiskScores.RemoveRange(_db.DeviceRiskScores);
        await _db.SaveChangesAsync();

        await SeedDecisionAsync(
            seed.ProductA.Id,
            RemediationOutcome.PatchingDeferred,
            approvalStatus: DecisionApprovalStatus.Approved,
            maintenanceWindowDate: null);

        var scoreAfter = await GetDeviceScoreAsync(seed.DeviceA.Id);

        scoreAfter.Should().Be(baseline,
            "PatchingDeferred is administrative only and must not change the score");
    }
}
