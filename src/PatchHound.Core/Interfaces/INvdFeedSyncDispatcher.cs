namespace PatchHound.Core.Interfaces;

public interface INvdFeedSyncDispatcher
{
    void QueueModifiedSync();
    void QueueFullSync(int fromYear, int toYear);
}
