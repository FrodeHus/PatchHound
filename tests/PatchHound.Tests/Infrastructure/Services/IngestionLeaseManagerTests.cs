using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure;

namespace PatchHound.Tests.Infrastructure.Services;

public class IngestionLeaseManagerTests : IAsyncDisposable
{
    private readonly Guid _tenantId;
    private readonly global::PatchHound.Infrastructure.Data.PatchHoundDbContext _db;

    public IngestionLeaseManagerTests()
    {
        _db = TestDbContextFactory.CreateTenantContext(out _tenantId);

        // Seed the TenantSourceConfiguration row required by TryAcquireIngestionRunAsync
        _db.TenantSourceConfigurations.Add(TenantSourceConfiguration.Create(
            _tenantId,
            sourceKey: "defender",
            displayName: "Defender",
            enabled: true,
            syncSchedule: "0 * * * *"
        ));
        _db.SaveChanges();
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private IngestionLeaseManager CreateSut() =>
        new(_db, new InMemoryIngestionBulkWriter(_db), NullLogger<IngestionLeaseManager>.Instance);

    [Fact]
    public async Task TryAcquireIngestionRunAsync_WhenNoActiveRun_ReturnsRun()
    {
        var sut = CreateSut();
        var result = await sut.TryAcquireIngestionRunAsync(_tenantId, "defender", CancellationToken.None);
        result.Should().NotBeNull();
        result!.Run.TenantId.Should().Be(_tenantId);
        result.Resumed.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireIngestionRunAsync_WhenAlreadyActive_ReturnsNull()
    {
        var sut = CreateSut();
        var first = await sut.TryAcquireIngestionRunAsync(_tenantId, "defender", CancellationToken.None);
        first.Should().NotBeNull();

        var second = await sut.TryAcquireIngestionRunAsync(_tenantId, "defender", CancellationToken.None);
        second.Should().BeNull();
    }

    [Fact]
    public async Task CompleteIngestionRunAsync_OnFailure_ClearsStagedData()
    {
        // Arrange: acquire a lease and add a staged device for the run
        var sut = CreateSut();
        var acquired = await sut.TryAcquireIngestionRunAsync(_tenantId, "defender", CancellationToken.None);
        acquired.Should().NotBeNull();

        _db.StagedDevices.Add(StagedDevice.Create(
            ingestionRunId: acquired!.Run.Id,
            tenantId: _tenantId,
            sourceKey: "defender",
            externalId: "device-001",
            name: "Device 001",
            assetType: AssetType.Device,
            payloadJson: "{}",
            stagedAt: DateTimeOffset.UtcNow
        ));
        await _db.SaveChangesAsync();

        // Act: complete as failure
        await sut.CompleteIngestionRunAsync(
            runId: acquired.Run.Id,
            tenantId: _tenantId,
            sourceKey: "defender",
            succeeded: false,
            error: "test failure",
            vulnerabilityMergeSummary: new StagedVulnerabilityMergeSummary(0, 0, 0, 0, 0, 0),
            assetMergeSummary: new StagedAssetMergeSummary(0, 0, 0, 0, 0),
            deactivatedMachineCount: 0,
            failureStatus: null,
            ct: CancellationToken.None);

        // Assert: staged device was cleaned up
        var remaining = await _db.StagedDevices.IgnoreQueryFilters()
            .Where(d => d.IngestionRunId == acquired.Run.Id).CountAsync();
        remaining.Should().Be(0, "failed runs should also clear staged data");
    }
}
