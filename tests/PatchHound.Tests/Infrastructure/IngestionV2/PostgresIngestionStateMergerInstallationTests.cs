using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Services.IngestionV2;

namespace PatchHound.Tests.Infrastructure.IngestionV2;

[Collection(PostgresCollection.Name)]
public sealed class PostgresIngestionStateMergerInstallationTests
{
    private static readonly Guid TenantId = Guid.Parse("00000001-0000-0000-0000-000000000001");

    private readonly PostgresFixture _fx;

    public PostgresIngestionStateMergerInstallationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task MergeInstallationsAsync_updates_current_run_devices_and_records_deltas()
    {
        await _fx.ResetAsync();
        await using var db = _fx.CreateDbContext();
        var observedAt = DateTimeOffset.UtcNow;
        var sourceSystem = SourceSystem.Create("defender-v2-install", "Defender V2 Install");
        var run = IngestionRun.Start(TenantId, sourceSystem.Key, observedAt);
        db.SourceSystems.Add(sourceSystem);
        db.IngestionRuns.Add(run);
        await db.SaveChangesAsync();

        var batch = IngestionObservationBatch.Create(
            TenantId,
            run.Id,
            sourceSystem.Id,
            devices: Enumerable.Range(1, 10)
                .Select(i => DeviceObservationInput.Create($"device-{i}", $"host-{i}", observedAt))
                .ToArray(),
            software: Enumerable.Range(1, 5)
                .Select(i => SoftwareObservationInput.Create(
                    $"software-{i}",
                    "Microsoft",
                    $"Product {i}",
                    "126.0",
                    observedAt))
                .ToArray(),
            installations: Enumerable.Range(1, 10)
                .SelectMany(device => Enumerable.Range(1, 5)
                    .Select(software => InstallationObservationInput.Create(
                        $"device-{device}",
                        $"software-{software}",
                        "126.0",
                        observedAt)))
                .ToArray());

        var loader = new PostgresObservationBulkLoader(db);
        await loader.LoadAsync(batch, CancellationToken.None);

        var merger = new PostgresIngestionStateMerger(db);
        await merger.MergeSoftwareAsync(TenantId, run.Id, CancellationToken.None);
        await merger.MergeDevicesAsync(TenantId, run.Id, CancellationToken.None);
        await merger.MergeInstallationsAsync(TenantId, run.Id, CancellationToken.None);

        (await db.Devices.CountAsync(d => d.TenantId == TenantId)).Should().Be(10);
        (await db.InstalledSoftware.CountAsync(i => i.TenantId == TenantId)).Should().Be(50);
        (await db.IngestionRunDeltas.CountAsync(d => d.RunId == run.Id && d.Kind == "Device")).Should().Be(10);
        (await db.IngestionRunDeltas.CountAsync(d => d.RunId == run.Id && d.Kind == "InstalledSoftware")).Should().Be(50);
    }
}
