namespace PatchHound.Api.Services;

public interface IApiClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemApiClock : IApiClock
{
    public static SystemApiClock Instance { get; } = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
