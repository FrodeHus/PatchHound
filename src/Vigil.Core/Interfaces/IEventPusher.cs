namespace Vigil.Core.Interfaces;

public interface IEventPusher
{
    Task PushAsync(string eventName, object data, string? userId = null, CancellationToken ct = default);
}
