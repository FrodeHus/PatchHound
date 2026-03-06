using Vigil.Core.Enums;

namespace Vigil.Core.Entities;

public class Notification
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public string? RelatedEntityType { get; private set; }
    public Guid? RelatedEntityId { get; private set; }
    public DateTimeOffset SentAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    private Notification() { }

    public static Notification Create(
        Guid userId,
        Guid tenantId,
        NotificationType type,
        string title,
        string body,
        string? relatedEntityType = null,
        Guid? relatedEntityId = null)
    {
        return new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            Type = type,
            Title = title,
            Body = body,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            SentAt = DateTimeOffset.UtcNow
        };
    }

    public void MarkAsRead()
    {
        ReadAt = DateTimeOffset.UtcNow;
    }
}
