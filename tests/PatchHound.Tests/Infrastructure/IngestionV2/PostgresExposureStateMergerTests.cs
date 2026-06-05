using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Services.IngestionV2;

namespace PatchHound.Tests.Infrastructure.IngestionV2;

[Collection(PostgresCollection.Name)]
public sealed class PostgresExposureStateMergerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000001-0000-0000-0000-000000000001");

    private readonly PostgresFixture _fx;

    public PostgresExposureStateMergerTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task MergeDirectExposuresAsync_opens_reobserves_and_resolves_by_touched_devices()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var sourceSystem = SourceSystem.Create("defender-v2-exposure", "Defender V2 Exposure");
        var firstObservedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var secondObservedAt = DateTimeOffset.UtcNow;
        var firstRun = IngestionRun.Start(TenantId, sourceSystem.Key, firstObservedAt);
        var secondRun = IngestionRun.Start(TenantId, sourceSystem.Key, secondObservedAt);
        db.SourceSystems.Add(sourceSystem);
        db.IngestionRuns.AddRange(firstRun, secondRun);
        await db.SaveChangesAsync();

        var loader = new PostgresObservationBulkLoader(db);
        await loader.LoadAsync(BuildBatch(sourceSystem.Id, firstRun.Id, firstObservedAt, includeResolvedVulnerability: true), CancellationToken.None);
        await loader.LoadAsync(BuildBatch(sourceSystem.Id, secondRun.Id, secondObservedAt, includeResolvedVulnerability: false), CancellationToken.None);

        var stateMerger = new PostgresIngestionStateMerger(db);
        var exposureMerger = new PostgresExposureStateMerger(db);

        await MergeStateAsync(stateMerger, firstRun.Id);
        await exposureMerger.MergeDirectExposuresAsync(TenantId, firstRun.Id, CancellationToken.None);

        await MergeStateAsync(stateMerger, secondRun.Id);
        await exposureMerger.MergeDirectExposuresAsync(TenantId, secondRun.Id, CancellationToken.None);

        var resolved = await db.DeviceVulnerabilityExposures
            .Include(e => e.Vulnerability)
            .SingleAsync(e => e.Vulnerability.ExternalId == "CVE-2026-RESOLVED");
        var stillOpen = await db.DeviceVulnerabilityExposures
            .Include(e => e.Vulnerability)
            .SingleAsync(e => e.Vulnerability.ExternalId == "CVE-2026-OPEN");

        resolved.Status.Should().Be(ExposureStatus.Resolved);
        resolved.ResolvedAt.Should().NotBeNull();
        stillOpen.Status.Should().Be(ExposureStatus.Open);
        stillOpen.LastSeenRunId.Should().Be(secondRun.Id);
    }

    private static IngestionObservationBatch BuildBatch(
        Guid sourceSystemId,
        Guid runId,
        DateTimeOffset observedAt,
        bool includeResolvedVulnerability)
    {
        var vulnerabilities = new List<VulnerabilityObservationInput>
        {
            VulnerabilityObservationInput.Create("CVE-2026-OPEN", "Open vuln", Severity.High, observedAt),
        };
        var exposures = new List<ExposureObservationInput>
        {
            ExposureObservationInput.Create("device-1", "CVE-2026-OPEN", "software-1", "126.0", observedAt),
        };

        if (includeResolvedVulnerability)
        {
            vulnerabilities.Add(VulnerabilityObservationInput.Create(
                "CVE-2026-RESOLVED",
                "Resolved vuln",
                Severity.High,
                observedAt));
            exposures.Add(ExposureObservationInput.Create(
                "device-1",
                "CVE-2026-RESOLVED",
                "software-1",
                "126.0",
                observedAt));
        }

        return IngestionObservationBatch.Create(
            TenantId,
            runId,
            sourceSystemId,
            devices:
            [
                DeviceObservationInput.Create("device-1", "host-1", observedAt),
            ],
            software:
            [
                SoftwareObservationInput.Create("software-1", "Microsoft", "Edge", "126.0", observedAt),
            ],
            installations:
            [
                InstallationObservationInput.Create("device-1", "software-1", "126.0", observedAt),
            ],
            vulnerabilities: vulnerabilities,
            exposures: exposures);
    }

    private static async Task MergeStateAsync(PostgresIngestionStateMerger merger, Guid runId)
    {
        await merger.MergeSoftwareAsync(TenantId, runId, CancellationToken.None);
        await merger.MergeDevicesAsync(TenantId, runId, CancellationToken.None);
        await merger.MergeInstallationsAsync(TenantId, runId, CancellationToken.None);
        await merger.MergeVulnerabilitiesAsync(TenantId, runId, CancellationToken.None);
    }
}
