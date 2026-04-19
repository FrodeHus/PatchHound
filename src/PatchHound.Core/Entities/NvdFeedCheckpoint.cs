namespace PatchHound.Core.Entities;

public class NvdFeedCheckpoint
{
    public string FeedName { get; private set; } = string.Empty;
    public DateTimeOffset FeedLastModified { get; private set; }
    public DateTimeOffset SyncedAt { get; private set; }

    private NvdFeedCheckpoint() { }

    public static NvdFeedCheckpoint Create(string feedName, DateTimeOffset feedLastModified)
    {
        if (string.IsNullOrWhiteSpace(feedName))
            throw new ArgumentException("FeedName is required.", nameof(feedName));
        if (feedName.Length > 32)
            throw new ArgumentException("FeedName must not exceed 32 characters.", nameof(feedName));
        return new NvdFeedCheckpoint
        {
            FeedName = feedName,
            FeedLastModified = feedLastModified,
            SyncedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(DateTimeOffset feedLastModified)
    {
        FeedLastModified = feedLastModified;
        SyncedAt = DateTimeOffset.UtcNow;
    }
}
