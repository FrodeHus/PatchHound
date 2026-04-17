namespace PatchHound.Core.Interfaces;

public interface IIngestionSource
{
    string SourceKey { get; }
    string SourceName { get; }
}
