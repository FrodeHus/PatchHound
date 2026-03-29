using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure;

public class AuditLogWriterTests : IDisposable
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly SentinelAuditQueue _queue;
    private readonly ITenantContext _tenantContext;

    public AuditLogWriterTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentUserId.Returns(Guid.NewGuid());
        _tenantContext.AccessibleTenantIds.Returns([]);
        _tenantContext.CurrentTenantId.Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );
        _queue = new SentinelAuditQueue();
    }

    [Fact]
    public async Task WriteAsync_persists_and_enqueues_audit_event()
    {
        var writer = new AuditLogWriter(_dbContext, _tenantContext, _queue);
        var tenantId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        await writer.WriteAsync(
            tenantId,
            "RoleActivation",
            entityId,
            AuditAction.Activated,
            null,
            new { Role = "Admin" },
            CancellationToken.None
        );
        await _dbContext.SaveChangesAsync();

        var entry = await _dbContext.AuditLogEntries.IgnoreQueryFilters().SingleAsync();
        entry.TenantId.Should().Be(tenantId);
        entry.EntityType.Should().Be("RoleActivation");
        entry.EntityId.Should().Be(entityId);
        entry.Action.Should().Be(AuditAction.Activated);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await foreach (var queued in _queue.ReadAllAsync(cts.Token))
        {
            queued.AuditEntryId.Should().Be(entry.Id);
            queued.TenantId.Should().Be(tenantId);
            queued.EntityType.Should().Be("RoleActivation");
            queued.EntityId.Should().Be(entityId);
            queued.Action.Should().Be(nameof(AuditAction.Activated));

            var newValues = JsonSerializer.Deserialize<Dictionary<string, string>>(queued.NewValues!);
            newValues.Should().NotBeNull();
            newValues!["Role"].Should().Be("Admin");
            return;
        }

        throw new Xunit.Sdk.XunitException("Expected queued Sentinel audit event.");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
