namespace PatchHound.Infrastructure.Services;

public sealed class IngestionAbortedException : Exception
{
    public IngestionAbortedException()
        : base("Ingestion was aborted by an operator.") { }
}
