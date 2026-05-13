using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PatchHound.Core.Entities;
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
        new(_db, NullLogger<IngestionLeaseManager>.Instance);

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
}
