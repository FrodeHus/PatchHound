using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PatchHound.Core.Enums;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure;

namespace PatchHound.Tests.Infrastructure.Services;

public class IngestionStagingPipelineTests : IAsyncDisposable
{
    private readonly Guid _tenantId;
    private readonly global::PatchHound.Infrastructure.Data.PatchHoundDbContext _db;

    public IngestionStagingPipelineTests()
    {
        _db = TestDbContextFactory.CreateTenantContext(out _tenantId);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private IngestionStagingPipeline CreateSut() => new(
        _db,
        new EnrichmentJobEnqueuer(_db, NullLogger<EnrichmentJobEnqueuer>.Instance),
        new IngestionLeaseManager(_db, new InMemoryIngestionBulkWriter(_db), NullLogger<IngestionLeaseManager>.Instance),
        new IngestionCheckpointWriter(_db)
    );

    private static IngestionResult MakeResult(string externalId, params string[] assetIds) =>
        new(
            ExternalId: externalId,
            Title: "Test Vuln",
            Description: "desc",
            VendorSeverity: Severity.High,
            CvssScore: null,
            CvssVector: null,
            PublishedDate: null,
            AffectedAssets: assetIds
                .Select(id => new IngestionAffectedAsset(id, id, AssetType.Device))
                .ToList()
        );

    [Fact]
    public async Task StageVulnerabilitiesAsync_EmptyResults_DoesNotWriteRows()
    {
        var sut = CreateSut();
        var runId = Guid.NewGuid();

        await sut.StageVulnerabilitiesAsync(runId, _tenantId, "test-source", [], 1, CancellationToken.None);

        _db.StagedVulnerabilities.Should().BeEmpty();
        _db.StagedVulnerabilityExposures.Should().BeEmpty();
    }

    [Fact]
    public async Task StageVulnerabilitiesAsync_WithResults_WritesExpectedRows()
    {
        var sut = CreateSut();
        var runId = Guid.NewGuid();
        var results = new List<IngestionResult> { MakeResult("CVE-2024-0001", "asset-1") };

        await sut.StageVulnerabilitiesAsync(runId, _tenantId, "test-source", results, 1, CancellationToken.None);

        _db.StagedVulnerabilities.Should().HaveCount(1);
        _db.StagedVulnerabilityExposures.Should().HaveCount(1);
    }

    [Fact]
    public void NormalizeResults_DuplicateExternalIds_DeduplicatesVulnerabilities()
    {
        var results = new List<IngestionResult>
        {
            MakeResult("CVE-2024-0001", "a1"),
            MakeResult("CVE-2024-0001", "a2"),
        };

        var normalized = IngestionStagingPipeline.NormalizeResults(results);

        normalized.Should().HaveCount(1);
        normalized[0].AffectedAssets.Should().HaveCount(2);
    }

    [Fact]
    public async Task StageAssetInventorySnapshotAsync_EmptySnapshot_DoesNotWriteRows()
    {
        var sut = CreateSut();
        var runId = Guid.NewGuid();
        var snapshot = new IngestionAssetInventorySnapshot([], []);

        await sut.StageAssetInventorySnapshotAsync(runId, _tenantId, "test-source", snapshot, 1, CancellationToken.None);

        _db.StagedDevices.Should().BeEmpty();
        _db.StagedDeviceSoftwareInstallations.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeAssetSnapshot_DuplicateAssetExternalIds_KeepsLast()
    {
        var snapshot = new IngestionAssetInventorySnapshot(
            Assets:
            [
                new IngestionAsset("ext-1", "First", AssetType.Device),
                new IngestionAsset("ext-1", "Second", AssetType.Device),
            ],
            DeviceSoftwareLinks: []
        );

        var normalized = IngestionStagingPipeline.NormalizeAssetSnapshot(snapshot);

        normalized.Assets.Should().HaveCount(1);
        normalized.Assets[0].Name.Should().Be("Second");
    }

    [Fact]
    public void Chunk_SplitsCorrectly()
    {
        var items = Enumerable.Range(1, 10).ToList();

        var chunks = IngestionStagingPipeline.Chunk(items, 3).ToList();

        chunks.Should().HaveCount(4);
        chunks[0].Should().HaveCount(3);
        chunks[3].Should().HaveCount(1);
    }
}
