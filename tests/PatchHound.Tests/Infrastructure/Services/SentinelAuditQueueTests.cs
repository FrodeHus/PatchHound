using PatchHound.Core.Models;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Tests.Infrastructure.Services;

public class SentinelAuditQueueTests
{
    private static SentinelAuditEvent CreateEvent(string entityType = "TestEntity") =>
        new(
            AuditEntryId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            EntityType: entityType,
            EntityId: Guid.NewGuid(),
            Action: "Created",
            OldValues: null,
            NewValues: """{"Name":"test"}""",
            UserId: Guid.NewGuid(),
            Timestamp: DateTimeOffset.UtcNow
        );

    [Fact]
    public void TryWrite_returns_true_when_channel_has_capacity()
    {
        var queue = new SentinelAuditQueue();
        var result = queue.TryWrite(CreateEvent());
        Assert.True(result);
    }

    [Fact]
    public async Task Written_event_can_be_read_back()
    {
        var queue = new SentinelAuditQueue();
        var ev = CreateEvent("Vulnerability");

        queue.TryWrite(ev);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await foreach (var item in queue.ReadAllAsync(cts.Token))
        {
            Assert.Equal("Vulnerability", item.EntityType);
            Assert.Equal(ev.AuditEntryId, item.AuditEntryId);
            return;
        }

        Assert.Fail("No event read from queue");
    }
}
