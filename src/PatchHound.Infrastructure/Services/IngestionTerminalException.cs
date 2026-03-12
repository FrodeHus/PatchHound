namespace PatchHound.Infrastructure.Services;

public class IngestionTerminalException : Exception
{
    public IngestionTerminalException(string message)
        : base(message) { }

    public IngestionTerminalException(string message, Exception innerException)
        : base(message, innerException) { }
}
