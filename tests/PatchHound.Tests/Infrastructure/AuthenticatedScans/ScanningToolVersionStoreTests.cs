using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.AuthenticatedScans;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;
using Xunit;

namespace PatchHound.Tests.Infrastructure.AuthenticatedScans;

public class ScanningToolVersionStoreTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private ScanningToolVersionStore _sut = null!;
    private ScanningTool _tool = null!;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(tenantContext));

        _tool = ScanningTool.Create(_tenantId, "tool-1", "desc", "python", "/usr/bin/python3", 300, "NormalizedSoftware");
        _db.ScanningTools.Add(_tool);
        await _db.SaveChangesAsync();

        _sut = new ScanningToolVersionStore(_db);
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task PublishNewVersion_creates_version_and_sets_current()
    {
        var v = await _sut.PublishNewVersionAsync(_tool.Id, "print('v1')", _userId, CancellationToken.None);

        Assert.Equal(1, v.VersionNumber);
        var tool = await _db.ScanningTools.SingleAsync(t => t.Id == _tool.Id);
        Assert.Equal(v.Id, tool.CurrentVersionId);
    }

    [Fact]
    public async Task PublishNewVersion_increments_version_number()
    {
        await _sut.PublishNewVersionAsync(_tool.Id, "v1", _userId, CancellationToken.None);
        var v2 = await _sut.PublishNewVersionAsync(_tool.Id, "v2", _userId, CancellationToken.None);

        Assert.Equal(2, v2.VersionNumber);
    }

    [Fact]
    public async Task PublishNewVersion_prunes_beyond_10_versions()
    {
        // Seed 10 versions
        for (var i = 1; i <= 10; i++)
        {
            await _sut.PublishNewVersionAsync(_tool.Id, $"v{i}", _userId, CancellationToken.None);
        }
        Assert.Equal(10, await _db.ScanningToolVersions.CountAsync(v => v.ScanningToolId == _tool.Id));

        // 11th should prune the oldest
        await _sut.PublishNewVersionAsync(_tool.Id, "v11", _userId, CancellationToken.None);

        var remaining = await _db.ScanningToolVersions
            .Where(v => v.ScanningToolId == _tool.Id)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync();
        Assert.Equal(10, remaining.Count);
        Assert.Equal(2, remaining.First().VersionNumber); // v1 pruned
        Assert.Equal(11, remaining.Last().VersionNumber);
    }
}
