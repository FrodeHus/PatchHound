using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class EmailNotificationService(
    PatchHoundDbContext dbContext,
    IEmailSender emailSender,
    ILogger<EmailNotificationService> logger
) : INotificationService
{

    public async Task SendAsync(
        Guid userId,
        Guid tenantId,
        NotificationType type,
        string title,
        string body,
        string? relatedEntityType = null,
        Guid? relatedEntityId = null,
        CancellationToken ct = default
    )
    {
        var notification = Notification.Create(
            userId,
            tenantId,
            type,
            title,
            body,
            relatedEntityType,
            relatedEntityId
        );
        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync(ct);

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null)
        {
            try
            {
                await emailSender.SendEmailAsync(user.Email, title, body, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to send notification email to user {UserId} for tenant {TenantId}.",
                    userId,
                    tenantId
                );
            }
        }
    }

    public async Task SendToTeamAsync(
        Guid teamId,
        Guid tenantId,
        NotificationType type,
        string title,
        string body,
        string? relatedEntityType = null,
        Guid? relatedEntityId = null,
        CancellationToken ct = default
    )
    {
        var members = await dbContext
            .TeamMembers.IgnoreQueryFilters()
            .Where(tm => tm.TeamId == teamId)
            .ToListAsync(ct);

        foreach (var member in members)
        {
            await SendAsync(
                member.UserId,
                tenantId,
                type,
                title,
                body,
                relatedEntityType,
                relatedEntityId,
                ct
            );
        }
    }
}
