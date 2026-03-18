using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure;

public class StagedAssetMergeServiceTagTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;

    public StagedAssetMergeServiceTagTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(tenantContext)
        );
    }

    [Fact]
    public async Task AssetTag_CanBeCreatedAndQueried()
    {
        var asset = Asset.Create(_tenantId, "dev-1", AssetType.Device, "Host", Criticality.Medium);
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        _dbContext.AssetTags.Add(AssetTag.Create(_tenantId, asset.Id, "production", "Defender"));
        _dbContext.AssetTags.Add(AssetTag.Create(_tenantId, asset.Id, "critical-infra", "Defender"));
        await _dbContext.SaveChangesAsync();

        var storedTags = await _dbContext.AssetTags
            .Where(t => t.AssetId == asset.Id)
            .Select(t => t.Tag)
            .ToListAsync();

        storedTags.Should().BeEquivalentTo("production", "critical-infra");
    }

    [Fact]
    public async Task AssetTag_RemoveWorks()
    {
        var asset = Asset.Create(_tenantId, "dev-2", AssetType.Device, "Host", Criticality.Medium);
        await _dbContext.Assets.AddAsync(asset);
        await _dbContext.SaveChangesAsync();

        var tag = AssetTag.Create(_tenantId, asset.Id, "old-tag", "Defender");
        _dbContext.AssetTags.Add(tag);
        await _dbContext.SaveChangesAsync();

        var existing = await _dbContext.AssetTags
            .Where(t => t.AssetId == asset.Id && t.Source == "Defender")
            .ToListAsync();
        _dbContext.AssetTags.RemoveRange(existing);
        await _dbContext.SaveChangesAsync();

        var remaining = await _dbContext.AssetTags
            .Where(t => t.AssetId == asset.Id)
            .CountAsync();
        remaining.Should().Be(0);
    }

    public void Dispose() => _dbContext.Dispose();
}
