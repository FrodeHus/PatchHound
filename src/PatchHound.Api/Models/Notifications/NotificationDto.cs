namespace PatchHound.Api.Models.Notifications;

public record NotificationDto(
    Guid Id,
    string Type,
    string Title,
    string Body,
    DateTimeOffset SentAt,
    DateTimeOffset? ReadAt,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    string? Path
);
