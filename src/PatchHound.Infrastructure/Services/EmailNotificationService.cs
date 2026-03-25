using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using System.Net;

namespace PatchHound.Infrastructure.Services;

public class EmailNotificationService(
    PatchHoundDbContext dbContext,
    IEmailSender emailSender,
    IConfiguration configuration,
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
                var emailBody = await BuildEmailBodyAsync(tenantId, type, title, body, relatedEntityType, relatedEntityId, ct);
                await emailSender.SendEmailAsync(user.Email, title, emailBody, ct);
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

    private async Task<string> BuildEmailBodyAsync(
        Guid tenantId,
        NotificationType type,
        string title,
        string body,
        string? relatedEntityType,
        Guid? relatedEntityId,
        CancellationToken ct
    )
    {
        if (relatedEntityType == "ApprovalTask" && relatedEntityId.HasValue)
        {
            var approvalContext = await BuildApprovalTaskEmailContextAsync(tenantId, relatedEntityId.Value, ct);
            if (approvalContext is not null)
                return RenderRemediationEmail(
                    title,
                    approvalContext.SoftwareName,
                    approvalContext.SeverityLabel,
                    approvalContext.AffectedDeviceCount,
                    approvalContext.StageLabel,
                    approvalContext.RequirementText,
                    approvalContext.PrimaryUrl,
                    approvalContext.PrimaryActionLabel,
                    approvalContext.SecondaryUrl,
                    approvalContext.SecondaryActionLabel,
                    body
                );
        }

        if (relatedEntityType == "PatchingTask" && relatedEntityId.HasValue)
        {
            var patchingContext = await BuildPatchingTaskEmailContextAsync(tenantId, relatedEntityId.Value, ct);
            if (patchingContext is not null)
                return RenderRemediationEmail(
                    title,
                    patchingContext.SoftwareName,
                    patchingContext.SeverityLabel,
                    patchingContext.AffectedDeviceCount,
                    patchingContext.StageLabel,
                    patchingContext.RequirementText,
                    patchingContext.PrimaryUrl,
                    patchingContext.PrimaryActionLabel,
                    patchingContext.SecondaryUrl,
                    patchingContext.SecondaryActionLabel,
                    body
                );
        }

        return RenderFallbackEmail(title, body);
    }

    private async Task<RemediationEmailContext?> BuildApprovalTaskEmailContextAsync(
        Guid tenantId,
        Guid approvalTaskId,
        CancellationToken ct
    )
    {
        var task = await dbContext.ApprovalTasks.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.Id == approvalTaskId)
            .Select(item => new
            {
                item.Id,
                item.Type,
                item.Status,
                item.RemediationDecision.TenantSoftwareId
            })
            .FirstOrDefaultAsync(ct);

        if (task is null)
            return null;

        var summary = await BuildRemediationSummaryAsync(tenantId, task.TenantSoftwareId, ct);
        if (summary is null)
            return null;

        var requirementText = task.Type switch
        {
            ApprovalTaskType.RiskAcceptanceApproval =>
                "Review the proposed risk acceptance or alternate mitigation and approve or deny whether it should become the active remediation posture.",
            ApprovalTaskType.PatchingApproved =>
                "Review the proposed patching approach and approve or deny whether execution should begin.",
            ApprovalTaskType.PatchingDeferred =>
                "Review the proposed patch deferral and decide whether it should replace immediate execution.",
            _ => "Review the proposed remediation stage and take the required action."
        };

        return new RemediationEmailContext(
            summary.SoftwareName,
            summary.SeverityLabel,
            summary.AffectedDeviceCount,
            "Approval",
            requirementText,
            $"{GetFrontendOrigin()}/approvals/{task.Id}",
            "Open approval task",
            $"{GetFrontendOrigin()}/software/{task.TenantSoftwareId}/remediation",
            "View remediation"
        );
    }

    private async Task<RemediationEmailContext?> BuildPatchingTaskEmailContextAsync(
        Guid tenantId,
        Guid patchingTaskId,
        CancellationToken ct
    )
    {
        var task = await dbContext.PatchingTasks.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.Id == patchingTaskId)
            .Select(item => new
            {
                item.Id,
                item.TenantSoftwareId,
                item.OwnerTeamId
            })
            .FirstOrDefaultAsync(ct);

        if (task is null)
            return null;

        var summary = await BuildRemediationSummaryAsync(tenantId, task.TenantSoftwareId, ct, task.OwnerTeamId);
        if (summary is null)
            return null;

        return new RemediationEmailContext(
            summary.SoftwareName,
            summary.SeverityLabel,
            summary.AffectedDeviceCount,
            "Execution",
            "Patch the affected devices owned by your team and update progress in PatchHound so the remediation can be tracked to completion.",
            $"{GetFrontendOrigin()}/remediation/tasks?tenantSoftwareId={task.TenantSoftwareId}",
            "Open remediation tasks",
            $"{GetFrontendOrigin()}/software/{task.TenantSoftwareId}/remediation",
            "View remediation"
        );
    }

    private async Task<RemediationSummary?> BuildRemediationSummaryAsync(
        Guid tenantId,
        Guid tenantSoftwareId,
        CancellationToken ct,
        Guid? ownerTeamId = null
    )
    {
        var softwareName = await dbContext.TenantSoftware.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.Id == tenantSoftwareId)
            .Join(
                dbContext.NormalizedSoftware.AsNoTracking(),
                tenantSoftware => tenantSoftware.NormalizedSoftwareId,
                normalizedSoftware => normalizedSoftware.Id,
                (tenantSoftware, normalizedSoftware) => normalizedSoftware.CanonicalName
            )
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(softwareName))
        {
            softwareName = await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.TenantSoftwareId == tenantSoftwareId && item.IsActive)
                .Join(
                    dbContext.Assets.AsNoTracking(),
                    item => item.SoftwareAssetId,
                    asset => asset.Id,
                    (item, asset) => asset.Name
                )
                .FirstOrDefaultAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(softwareName))
            return null;

        var severity = await dbContext.NormalizedSoftwareVulnerabilityProjections.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId
                && item.TenantSoftwareId == tenantSoftwareId
                && item.ResolvedAt == null)
            .Join(
                dbContext.VulnerabilityDefinitions.AsNoTracking(),
                projection => projection.VulnerabilityDefinitionId,
                definition => definition.Id,
                (projection, definition) => definition.VendorSeverity
            )
            .OrderByDescending(item => item)
            .FirstOrDefaultAsync(ct);

        var affectedDeviceCount = ownerTeamId.HasValue
            ? await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
                .Where(item =>
                    item.TenantId == tenantId
                    && item.TenantSoftwareId == tenantSoftwareId
                    && item.IsActive)
                .Join(
                    dbContext.Assets.AsNoTracking(),
                    item => item.DeviceAssetId,
                    asset => asset.Id,
                    (item, asset) => new { item.DeviceAssetId, TeamId = asset.OwnerTeamId ?? asset.FallbackTeamId }
                )
                .Where(item => item.TeamId == ownerTeamId.Value)
                .Select(item => item.DeviceAssetId)
                .Distinct()
                .CountAsync(ct)
            : await dbContext.NormalizedSoftwareInstallations.AsNoTracking()
                .Where(item =>
                    item.TenantId == tenantId
                    && item.TenantSoftwareId == tenantSoftwareId
                    && item.IsActive)
                .Select(item => item.DeviceAssetId)
                .Distinct()
                .CountAsync(ct);

        return new RemediationSummary(
            softwareName,
            (severity == default ? Severity.Medium : severity).ToString(),
            affectedDeviceCount
        );
    }

    private string GetFrontendOrigin() =>
        (configuration["FRONTEND_ORIGIN"] ?? "http://localhost:3000").TrimEnd('/');

    private static string RenderRemediationEmail(
        string title,
        string softwareName,
        string severityLabel,
        int affectedDeviceCount,
        string stageLabel,
        string requirementText,
        string primaryUrl,
        string primaryActionLabel,
        string secondaryUrl,
        string secondaryActionLabel,
        string body
    )
    {
        var encodedTitle = WebUtility.HtmlEncode(title);
        var encodedSoftwareName = WebUtility.HtmlEncode(softwareName);
        var encodedSeverity = WebUtility.HtmlEncode(severityLabel);
        var encodedStage = WebUtility.HtmlEncode(stageLabel);
        var encodedRequirement = WebUtility.HtmlEncode(requirementText);
        var encodedBody = WebUtility.HtmlEncode(body);
        var encodedPrimaryUrl = WebUtility.HtmlEncode(primaryUrl);
        var encodedSecondaryUrl = WebUtility.HtmlEncode(secondaryUrl);
        var encodedPrimaryAction = WebUtility.HtmlEncode(primaryActionLabel);
        var encodedSecondaryAction = WebUtility.HtmlEncode(secondaryActionLabel);

        return $$"""
<!DOCTYPE html>
<html lang="en">
  <body style="margin:0;background:#f4f7fb;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;color:#172033;">
    <div style="max-width:680px;margin:0 auto;padding:32px 20px;">
      <div style="background:#ffffff;border:1px solid #d8e2f0;border-radius:20px;overflow:hidden;box-shadow:0 12px 40px rgba(23,32,51,0.08);">
        <div style="padding:28px 28px 20px;background:linear-gradient(135deg,#10243d 0%,#1f4f7a 100%);color:#ffffff;">
          <div style="font-size:12px;letter-spacing:0.16em;text-transform:uppercase;opacity:0.78;">PatchHound Remediation</div>
          <h1 style="margin:10px 0 0;font-size:28px;line-height:1.2;">{{encodedTitle}}</h1>
        </div>
        <div style="padding:24px 28px 28px;">
          <div style="margin-bottom:20px;padding:18px 20px;border:1px solid #d8e2f0;border-radius:16px;background:#f8fbff;">
            <div style="font-size:13px;letter-spacing:0.12em;text-transform:uppercase;color:#5b6b86;margin-bottom:10px;">In Scope</div>
            <div style="font-size:24px;font-weight:700;color:#172033;margin-bottom:14px;">{{encodedSoftwareName}}</div>
            <div>
              <span style="display:inline-block;padding:6px 10px;border-radius:999px;background:#fff0cf;color:#7c5a00;font-size:13px;font-weight:600;margin-right:8px;">Severity: {{encodedSeverity}}</span>
              <span style="display:inline-block;padding:6px 10px;border-radius:999px;background:#e7f1ff;color:#124272;font-size:13px;font-weight:600;margin-right:8px;">Affected devices: {{affectedDeviceCount}}</span>
              <span style="display:inline-block;padding:6px 10px;border-radius:999px;background:#eaf7ee;color:#21623e;font-size:13px;font-weight:600;">Stage: {{encodedStage}}</span>
            </div>
          </div>

          <div style="margin-bottom:20px;">
            <div style="font-size:13px;letter-spacing:0.12em;text-transform:uppercase;color:#5b6b86;margin-bottom:8px;">What this stage requires</div>
            <div style="font-size:16px;line-height:1.6;color:#243247;">{{encodedRequirement}}</div>
          </div>

          <div style="margin-bottom:24px;padding:16px 18px;border-left:4px solid #5d88ff;background:#f7faff;border-radius:12px;">
            <div style="font-size:14px;line-height:1.6;color:#32425f;">{{encodedBody}}</div>
          </div>

          <div style="margin-bottom:12px;">
            <a href="{{encodedPrimaryUrl}}" style="display:inline-block;background:#2c6bed;color:#ffffff;text-decoration:none;padding:12px 18px;border-radius:12px;font-weight:700;">{{encodedPrimaryAction}}</a>
          </div>
          <div>
            <a href="{{encodedSecondaryUrl}}" style="color:#2c6bed;text-decoration:none;font-weight:600;">{{encodedSecondaryAction}}</a>
          </div>
        </div>
      </div>
    </div>
  </body>
</html>
""";
    }

    private static string RenderFallbackEmail(string title, string body)
    {
        var encodedTitle = WebUtility.HtmlEncode(title);
        var encodedBody = WebUtility.HtmlEncode(body).Replace("\n", "<br />");
        return $$"""
<!DOCTYPE html>
<html lang="en">
  <body style="margin:0;background:#f4f7fb;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;color:#172033;">
    <div style="max-width:640px;margin:0 auto;padding:32px 20px;">
      <div style="background:#ffffff;border:1px solid #d8e2f0;border-radius:18px;padding:24px;">
        <h1 style="margin:0 0 16px;font-size:24px;">{{encodedTitle}}</h1>
        <div style="font-size:15px;line-height:1.7;">{{encodedBody}}</div>
      </div>
    </div>
  </body>
</html>
""";
    }

    private sealed record RemediationSummary(string SoftwareName, string SeverityLabel, int AffectedDeviceCount);

    private sealed record RemediationEmailContext(
        string SoftwareName,
        string SeverityLabel,
        int AffectedDeviceCount,
        string StageLabel,
        string RequirementText,
        string PrimaryUrl,
        string PrimaryActionLabel,
        string SecondaryUrl,
        string SecondaryActionLabel
    );
}
