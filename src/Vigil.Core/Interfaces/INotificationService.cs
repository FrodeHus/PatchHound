using Vigil.Core.Enums;

namespace Vigil.Core.Interfaces;

public interface INotificationService
{
    Task SendAsync(Guid userId, Guid tenantId, NotificationType type, string title, string body, string? relatedEntityType = null, Guid? relatedEntityId = null, CancellationToken ct = default);
    Task SendToTeamAsync(Guid teamId, Guid tenantId, NotificationType type, string title, string body, string? relatedEntityType = null, Guid? relatedEntityId = null, CancellationToken ct = default);
}
