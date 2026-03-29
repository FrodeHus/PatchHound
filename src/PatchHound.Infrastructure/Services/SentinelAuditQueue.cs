using System.Threading.Channels;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.Services;

public sealed class SentinelAuditQueue
{
    private readonly Channel<SentinelAuditEvent> _channel = Channel.CreateBounded<SentinelAuditEvent>(
        new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        }
    );

    public bool TryWrite(SentinelAuditEvent auditEvent) => _channel.Writer.TryWrite(auditEvent);

    public IAsyncEnumerable<SentinelAuditEvent> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
