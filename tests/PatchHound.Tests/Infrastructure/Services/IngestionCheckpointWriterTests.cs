using FluentAssertions;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure;

namespace PatchHound.Tests.Infrastructure.Services;

public class IngestionCheckpointWriterTests : IAsyncDisposable
{
    private readonly Guid _tenantId;
    private readonly global::PatchHound.Infrastructure.Data.PatchHoundDbContext _db;

    public IngestionCheckpointWriterTests()
    {
        _db = TestDbContextFactory.CreateTenantContext(out _tenantId);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private IngestionCheckpointWriter CreateSut() => new(_db);

    [Fact]
    public async Task IsCheckpointCompletedAsync_BeforeCommit_ReturnsFalse()
    {
        var sut = CreateSut();
        var result = await sut.IsCheckpointCompletedAsync(
            Guid.NewGuid(), CheckpointPhases.AssetStaging, CancellationToken.None);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CommitCheckpointAsync_ThenIsCompleted_ReturnsTrue()
    {
        var runId = Guid.NewGuid();
        var sut = CreateSut();
        await sut.CommitCheckpointAsync(
            runId, _tenantId, "defender",
            CheckpointPhases.AssetStaging, 1, null, 10,
            CheckpointStatuses.Completed, CancellationToken.None);

        var result = await sut.IsCheckpointCompletedAsync(
            runId, CheckpointPhases.AssetStaging, CancellationToken.None);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetCheckpointBatchNumberAsync_AfterCommit_ReturnsBatchNumber()
    {
        var runId = Guid.NewGuid();
        var sut = CreateSut();
        await sut.CommitCheckpointAsync(
            runId, _tenantId, "defender",
            CheckpointPhases.VulnerabilityStaging, 7, null, 50,
            CheckpointStatuses.Running, CancellationToken.None);

        var batch = await sut.GetCheckpointBatchNumberAsync(
            runId, CheckpointPhases.VulnerabilityStaging, CancellationToken.None);
        batch.Should().Be(7);
    }
}
