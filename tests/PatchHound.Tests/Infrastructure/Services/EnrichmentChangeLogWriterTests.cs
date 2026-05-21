using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure.Services;

public class EnrichmentChangeLogWriterTests : IDisposable
{
    private readonly PatchHoundDbContext _db;

    public EnrichmentChangeLogWriterTests()
    {
        _db = CreateDbContext();
    }

    [Fact]
    public async Task WriteScalarChangesAsync_writes_changed_global_number()
    {
        var writer = CreateWriter(_db);
        var entityId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var changedAt = DateTimeOffset.Parse("2026-05-20T08:15:00Z");

        await writer.WriteScalarChangesAsync(
            new EnrichmentChangeSet(
                EnrichmentChangeScope.Global,
                null,
                "SoftwareProduct",
                entityId,
                "NVD",
                runId,
                jobId,
                changedAt
            ),
            [EnrichmentScalarChange.Number("risk.score", "Risk score", 7.50m, 8.8m)],
            CancellationToken.None
        );

        var row = await _db.EnrichmentChangeLogs.IgnoreQueryFilters().SingleAsync();
        row.Scope.Should().Be(EnrichmentChangeScope.Global);
        row.TenantId.Should().BeNull();
        row.EntityType.Should().Be("SoftwareProduct");
        row.EntityId.Should().Be(entityId);
        row.SourceKey.Should().Be("nvd");
        row.EnrichmentRunId.Should().Be(runId);
        row.EnrichmentJobId.Should().Be(jobId);
        row.FieldPath.Should().Be("risk.score");
        row.DisplayName.Should().Be("Risk score");
        row.OldValueJson.Should().Be("7.5");
        row.NewValueJson.Should().Be("8.8");
        row.ValueKind.Should().Be(EnrichmentChangeValueKind.Number);
        row.ChangedAt.Should().Be(changedAt);
    }

    [Fact]
    public async Task WriteScalarChangesAsync_skips_semantically_equal_numbers()
    {
        var writer = CreateWriter(_db);

        await writer.WriteScalarChangesAsync(
            CreateChangeSet(EnrichmentChangeScope.Global, null),
            [EnrichmentScalarChange.Number("risk.score", "Risk score", 7.50m, 7.5m)],
            CancellationToken.None
        );

        var count = await _db.EnrichmentChangeLogs.IgnoreQueryFilters().CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task WriteScalarChangesAsync_writes_tenant_scoped_string_values()
    {
        var tenantId = Guid.NewGuid();
        var writer = CreateWriter(_db);

        await writer.WriteScalarChangesAsync(
            CreateChangeSet(EnrichmentChangeScope.Tenant, tenantId),
            [EnrichmentScalarChange.String("lifecycle.status", "Lifecycle status", "Active", "EOL")],
            CancellationToken.None
        );

        var row = await _db.EnrichmentChangeLogs.IgnoreQueryFilters().SingleAsync();
        row.Scope.Should().Be(EnrichmentChangeScope.Tenant);
        row.TenantId.Should().Be(tenantId);
        row.OldValueJson.Should().Be("\"Active\"");
        row.NewValueJson.Should().Be("\"EOL\"");
        row.ValueKind.Should().Be(EnrichmentChangeValueKind.String);
    }

    [Fact]
    public async Task WriteScalarChangesAsync_can_stage_rows_without_saving()
    {
        var writer = CreateWriter(_db);

        await writer.WriteScalarChangesAsync(
            CreateChangeSet(EnrichmentChangeScope.Global, null),
            [EnrichmentScalarChange.String("lifecycle.status", "Lifecycle status", "Active", "EOL")],
            CancellationToken.None,
            saveChanges: false
        );

        (await _db.EnrichmentChangeLogs.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        _db.ChangeTracker
            .Entries<EnrichmentChangeLog>()
            .Should()
            .ContainSingle(entry => entry.State == EntityState.Added);

        await _db.SaveChangesAsync();

        (await _db.EnrichmentChangeLogs.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task WriteScalarChangesAsync_detaches_added_change_rows_when_save_fails()
    {
        await using var failingDb = new ThrowingSaveChangesDbContext(CreateOptions());
        var writer = CreateWriter(failingDb);

        await writer.WriteScalarChangesAsync(
            CreateChangeSet(EnrichmentChangeScope.Global, null),
            [EnrichmentScalarChange.String("lifecycle.status", "Lifecycle status", "Active", "EOL")],
            CancellationToken.None
        );

        var addedRows = failingDb.ChangeTracker
            .Entries<EnrichmentChangeLog>()
            .Where(entry => entry.State == EntityState.Added)
            .ToList();
        addedRows.Should().BeEmpty();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private static EnrichmentChangeLogWriter CreateWriter(PatchHoundDbContext db) =>
        new(db, Substitute.For<ILogger<EnrichmentChangeLogWriter>>());

    private static EnrichmentChangeSet CreateChangeSet(EnrichmentChangeScope scope, Guid? tenantId) =>
        new(
            scope,
            tenantId,
            "SoftwareProduct",
            Guid.NewGuid(),
            "Defender",
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-05-20T08:15:00Z")
        );

    private static PatchHoundDbContext CreateDbContext() =>
        new(CreateOptions(), TestServiceProviderFactory.Create(Substitute.For<ITenantContext>()));

    private static DbContextOptions<PatchHoundDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private sealed class ThrowingSaveChangesDbContext(
        DbContextOptions<PatchHoundDbContext> options
    ) : PatchHoundDbContext(options, TestServiceProviderFactory.Create(Substitute.For<ITenantContext>()))
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Save failed.");
    }
}
