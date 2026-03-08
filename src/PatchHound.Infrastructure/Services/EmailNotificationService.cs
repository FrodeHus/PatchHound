using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public class EmailNotificationService : INotificationService
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly IEmailSender _emailSender;

    public EmailNotificationService(PatchHoundDbContext dbContext, IEmailSender emailSender)
    {
        _dbContext = dbContext;
        _emailSender = emailSender;
    }

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
        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(ct);

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null)
        {
            await _emailSender.SendEmailAsync(user.Email, title, body, ct);
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
        var members = await _dbContext
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
