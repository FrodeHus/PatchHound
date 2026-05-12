namespace PatchHound.Core.Interfaces;

public interface INvdFeedSyncService
{
    Task SyncModifiedFeedAsync(CancellationToken ct);
    Task SyncYearFeedAsync(int year, CancellationToken ct);
    Task SyncYearFeedAsync(int year, bool force, CancellationToken ct);
}
