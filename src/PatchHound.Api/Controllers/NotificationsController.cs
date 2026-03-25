using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Models.Notifications;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController(
    PatchHoundDbContext dbContext,
    ITenantContext tenantContext
) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> List(
        [FromQuery] int take = 8,
        CancellationToken ct = default
    )
    {
        var userId = tenantContext.CurrentUserId;
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var items = await dbContext.Notifications.AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.ReadAt == null)
            .ThenByDescending(item => item.SentAt)
            .Take(Math.Clamp(take, 1, 20))
            .Select(item => new NotificationDto(
                item.Id,
                item.Type.ToString(),
                item.Title,
                item.Body,
                item.SentAt,
                item.ReadAt,
                item.RelatedEntityType,
                item.RelatedEntityId,
                item.RelatedEntityType == "Asset" && item.RelatedEntityId != null
                    ? $"/assets/{item.RelatedEntityId}"
                    : item.RelatedEntityType == "TenantVulnerability" && item.RelatedEntityId != null
                        ? $"/vulnerabilities/{item.RelatedEntityId}"
                        : item.RelatedEntityType == "ApprovalTask" && item.RelatedEntityId != null
                            ? $"/approvals/{item.RelatedEntityId}"
                            : item.RelatedEntityType == "PatchingTask"
                                ? "/remediation"
                                : item.RelatedEntityType == "RemediationTask"
                                    ? "/remediation"
                                : null
            ))
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> GetUnreadCount(CancellationToken ct)
    {
        var userId = tenantContext.CurrentUserId;
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var count = await dbContext.Notifications
            .Where(item => item.UserId == userId && item.ReadAt == null)
            .CountAsync(ct);

        return Ok(count);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var userId = tenantContext.CurrentUserId;
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var notification = await dbContext.Notifications
            .FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId, ct);
        if (notification is null)
        {
            return NotFound();
        }

        notification.MarkAsRead();
        await dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        var userId = tenantContext.CurrentUserId;
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var notifications = await dbContext.Notifications
            .Where(item => item.UserId == userId && item.ReadAt == null)
            .ToListAsync(ct);

        foreach (var notification in notifications)
        {
            notification.MarkAsRead();
        }

        await dbContext.SaveChangesAsync(ct);
        return NoContent();
    }
}
