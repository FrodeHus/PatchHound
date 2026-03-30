using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.System;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/system")]
[Authorize]
public class SystemController : ControllerBase
{
    private readonly ISecretStore _secretStore;
    private readonly PatchHoundDbContext _dbContext;
    private readonly AuditLogWriter _auditLogWriter;
    private readonly NotificationEmailConfigurationResolver _notificationConfigurationResolver;
    private readonly MailgunEmailSender _mailgunEmailSender;
    private readonly ITenantContext _tenantContext;

    public SystemController(
        ISecretStore secretStore,
        PatchHoundDbContext dbContext,
        AuditLogWriter auditLogWriter,
        NotificationEmailConfigurationResolver notificationConfigurationResolver,
        MailgunEmailSender mailgunEmailSender,
        ITenantContext tenantContext
    )
    {
        _secretStore = secretStore;
        _dbContext = dbContext;
        _auditLogWriter = auditLogWriter;
        _notificationConfigurationResolver = notificationConfigurationResolver;
        _mailgunEmailSender = mailgunEmailSender;
        _tenantContext = tenantContext;
    }

    [HttpGet("/api/health")]
    [AllowAnonymous]
    public IActionResult Health()
    {
        return Ok(new { status = "ok" });
    }

    [HttpGet("status")]
    public async Task<ActionResult<SystemStatusDto>> GetStatus(CancellationToken ct)
    {
        var status = await _secretStore.GetStatusAsync(ct);
        return Ok(new SystemStatusDto(status.IsAvailable, status.IsInitialized, status.IsSealed));
    }

    [HttpGet("notification-providers")]
    [Authorize(Policy = Policies.ManageGlobalSettings)]
    public async Task<ActionResult<NotificationProviderSettingsDto>> GetNotificationProviders(
        CancellationToken ct
    )
    {
        var configuration = await _notificationConfigurationResolver.GetAsync(ct);
        return Ok(
            new NotificationProviderSettingsDto(
                configuration.ActiveProvider,
                new SmtpNotificationProviderDto(
                    configuration.Smtp.Host,
                    configuration.Smtp.Port,
                    configuration.Smtp.Username,
                    configuration.Smtp.FromAddress,
                    configuration.Smtp.EnableSsl
                ),
                new MailgunNotificationProviderDto(
                    configuration.Mailgun.Enabled,
                    configuration.Mailgun.Region,
                    configuration.Mailgun.Domain,
                    configuration.Mailgun.FromAddress,
                    configuration.Mailgun.FromName,
                    configuration.Mailgun.ReplyToAddress,
                    !string.IsNullOrWhiteSpace(configuration.Mailgun.ApiKey)
                )
            )
        );
    }

    [HttpPut("notification-providers")]
    [Authorize(Policy = Policies.ManageGlobalSettings)]
    public async Task<IActionResult> UpdateNotificationProviders(
        [FromBody] UpdateNotificationProviderSettingsRequest request,
        CancellationToken ct
    )
    {
        var current = await _notificationConfigurationResolver.GetAsync(ct);
        var normalizedProvider = NotificationEmailConfigurationResolver.NormalizeProvider(
            request.ActiveProvider
        );
        var normalizedRegion = NotificationEmailConfigurationResolver.NormalizeRegion(
            request.Mailgun.Region
        );

        if (normalizedProvider == "mailgun")
        {
            if (string.IsNullOrWhiteSpace(request.Mailgun.Domain))
            {
                return BadRequest(new ProblemDetails { Title = "Mailgun domain is required." });
            }

            if (string.IsNullOrWhiteSpace(request.Mailgun.FromAddress))
            {
                return BadRequest(
                    new ProblemDetails { Title = "Mailgun from address is required." }
                );
            }

            if (
                string.IsNullOrWhiteSpace(request.Mailgun.ApiKey)
                && string.IsNullOrWhiteSpace(current.Mailgun.ApiKey)
            )
            {
                return BadRequest(new ProblemDetails { Title = "Mailgun API key is required." });
            }
        }

        var values = new Dictionary<string, string>
        {
            ["activeProvider"] = normalizedProvider,
            ["enabled"] = request.Mailgun.Enabled ? "true" : "false",
            ["region"] = normalizedRegion,
            ["domain"] = request.Mailgun.Domain.Trim(),
            ["fromAddress"] = request.Mailgun.FromAddress.Trim(),
            ["fromName"] = request.Mailgun.FromName?.Trim() ?? string.Empty,
            ["replyToAddress"] = request.Mailgun.ReplyToAddress?.Trim() ?? string.Empty,
        };
        if (!string.IsNullOrWhiteSpace(request.Mailgun.ApiKey))
        {
            values["apiKey"] = request.Mailgun.ApiKey.Trim();
        }

        await _secretStore.PutSecretAsync(
            NotificationEmailConfigurationResolver.MailgunSecretPath,
            values,
            ct
        );

        await _auditLogWriter.WriteAsync(
            Guid.Empty,
            "NotificationProviderSettings",
            Guid.Empty,
            AuditAction.Updated,
            new
            {
                current.ActiveProvider,
                Mailgun = new
                {
                    current.Mailgun.Enabled,
                    current.Mailgun.Region,
                    current.Mailgun.Domain,
                    current.Mailgun.FromAddress,
                    current.Mailgun.FromName,
                    current.Mailgun.ReplyToAddress,
                    HasApiKey = !string.IsNullOrWhiteSpace(current.Mailgun.ApiKey),
                },
            },
            new
            {
                ActiveProvider = normalizedProvider,
                Mailgun = new
                {
                    request.Mailgun.Enabled,
                    Region = normalizedRegion,
                    Domain = request.Mailgun.Domain.Trim(),
                    FromAddress = request.Mailgun.FromAddress.Trim(),
                    FromName = request.Mailgun.FromName?.Trim(),
                    ReplyToAddress = request.Mailgun.ReplyToAddress?.Trim(),
                    HasApiKey =
                        !string.IsNullOrWhiteSpace(request.Mailgun.ApiKey)
                        || !string.IsNullOrWhiteSpace(current.Mailgun.ApiKey),
                },
            },
            ct
        );
        await _dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("notification-providers/mailgun/validate")]
    [Authorize(Policy = Policies.ManageGlobalSettings)]
    public async Task<ActionResult<NotificationProviderValidationResponseDto>> ValidateMailgun(
        CancellationToken ct
    )
    {
        var configuration = await _notificationConfigurationResolver.GetAsync(ct);
        if (!configuration.Mailgun.IsConfigured)
        {
            return BadRequest(
                new ProblemDetails
                {
                    Title = "Mailgun is not fully configured. Domain, from address, and API key are required.",
                }
            );
        }

        var result = await _mailgunEmailSender.ValidateAsync(configuration.Mailgun, ct);
        if (!result.IsValid)
        {
            return BadRequest(
                new NotificationProviderValidationResponseDto(
                    false,
                    result.Message,
                    result.DomainState
                )
            );
        }

        return Ok(
            new NotificationProviderValidationResponseDto(
                true,
                result.Message,
                result.DomainState
            )
        );
    }

    [HttpPost("notification-providers/mailgun/test")]
    [Authorize(Policy = Policies.ManageGlobalSettings)]
    public async Task<ActionResult<NotificationProviderValidationResponseDto>> SendMailgunTestEmail(
        CancellationToken ct
    )
    {
        var configuration = await _notificationConfigurationResolver.GetAsync(ct);
        if (!configuration.Mailgun.IsConfigured)
        {
            return BadRequest(
                new ProblemDetails
                {
                    Title = "Mailgun is not fully configured. Domain, from address, and API key are required.",
                }
            );
        }

        if (_tenantContext.CurrentUserId == Guid.Empty)
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == _tenantContext.CurrentUserId, ct);
        if (user is null)
        {
            return NotFound(new ProblemDetails { Title = "Current user was not found." });
        }

        await _mailgunEmailSender.SendEmailAsync(
            configuration.Mailgun,
            user.Email,
            "PatchHound Mailgun test notification",
            "<p>This is a test notification from PatchHound using the configured Mailgun delivery settings.</p>",
            ct
        );

        return Ok(
            new NotificationProviderValidationResponseDto(
                true,
                $"Sent a test email to {user.Email}.",
                null
            )
        );
    }

    [HttpPost("openbao/unseal")]
    [Authorize(Policy = Policies.ManageVault)]
    public async Task<ActionResult<SystemStatusDto>> Unseal(
        [FromBody] OpenBaoUnsealRequest request,
        CancellationToken ct
    )
    {
        var keys = request
            .Keys.Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .ToList();

        if (keys.Count < 3)
        {
            return BadRequest(new ProblemDetails { Title = "Three unseal keys are required." });
        }

        var status = await _secretStore.UnsealAsync(keys, ct);
        return Ok(new SystemStatusDto(status.IsAvailable, status.IsInitialized, status.IsSealed));
    }

    [HttpGet("enrichment-sources")]
    [Authorize(Policy = Policies.ManageGlobalSettings)]
    public async Task<ActionResult<IReadOnlyList<EnrichmentSourceDto>>> GetEnrichmentSources(
        CancellationToken ct
    )
    {
        var persistedSources = await _dbContext
            .EnrichmentSourceConfigurations.AsNoTracking()
            .ToListAsync(ct);
        var sources = EnrichmentSourceCatalog
            .CreateDefaults()
            .Concat(persistedSources)
            .GroupBy(source => source.SourceKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(source => source.DisplayName)
            .ToList();

        var sourceKeys = sources.Select(source => source.SourceKey).ToList();
        var queueRows = await _dbContext
            .EnrichmentJobs.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(job => sourceKeys.Contains(job.SourceKey))
            .Select(job => new
            {
                job.SourceKey,
                job.Status,
                job.NextAttemptAt,
            })
            .ToListAsync(ct);
        var queueBySourceKey = queueRows
            .GroupBy(row => row.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var pendingRows = group
                        .Where(row =>
                            row.Status == EnrichmentJobStatus.Pending
                            || row.Status == EnrichmentJobStatus.RetryScheduled
                        )
                        .ToList();
                    return new EnrichmentSourceQueueDto(
                        group.Count(row => row.Status == EnrichmentJobStatus.Pending),
                        group.Count(row => row.Status == EnrichmentJobStatus.RetryScheduled),
                        group.Count(row => row.Status == EnrichmentJobStatus.Running),
                        group.Count(row => row.Status == EnrichmentJobStatus.Failed),
                        pendingRows.Count == 0 ? null : pendingRows.Min(row => row.NextAttemptAt)
                    );
                },
                StringComparer.OrdinalIgnoreCase
            );

        var recentRuns = await _dbContext
            .EnrichmentRuns.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(run => sourceKeys.Contains(run.SourceKey))
            .OrderByDescending(run => run.StartedAt)
            .ToListAsync(ct);
        var recentRunsBySourceKey = recentRuns
            .GroupBy(run => run.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Take(5).Select(MapRunDto).ToList().AsReadOnly(),
                StringComparer.OrdinalIgnoreCase
            );

        return Ok(
            sources
                .Select(source =>
                    MapEnrichmentSourceDto(
                        source,
                        queueBySourceKey.GetValueOrDefault(
                            source.SourceKey,
                            new EnrichmentSourceQueueDto(0, 0, 0, 0, null)
                        ),
                        recentRunsBySourceKey.GetValueOrDefault(source.SourceKey, [])
                    )
                )
                .ToList()
        );
    }

    [HttpGet("enrichment-sources/{sourceKey}/runs")]
    [Authorize(Policy = Policies.ManageGlobalSettings)]
    public async Task<ActionResult<PagedResponse<EnrichmentRunDto>>> GetEnrichmentRuns(
        string sourceKey,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var query = _dbContext
            .EnrichmentRuns.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(run => run.SourceKey == normalizedSourceKey);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(run => run.StartedAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(run => MapRunDto(run))
            .ToListAsync(ct);

        return Ok(
            new PagedResponse<EnrichmentRunDto>(
                items,
                totalCount,
                pagination.Page,
                pagination.BoundedPageSize
            )
        );
    }

    [HttpPut("enrichment-sources")]
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<IActionResult> UpdateEnrichmentSources(
        [FromBody] List<UpdateEnrichmentSourceRequest> request,
        CancellationToken ct
    )
    {
        var existingSources = await _dbContext.EnrichmentSourceConfigurations.ToDictionaryAsync(
            source => source.SourceKey,
            StringComparer.OrdinalIgnoreCase,
            ct
        );

        var pendingSecretWrites = new List<(string Path, string Key, string Value)>();
        var pendingSecretAudits = new List<(Guid EntityId, string? OldSecretRef, string NewSecretRef)>();

        foreach (var source in request)
        {
            existingSources.TryGetValue(source.Key, out var existingSource);
            var secretRef = existingSource?.SecretRef ?? string.Empty;
            var secretValue = source.Credentials.Secret.Trim();
            var oldSecretRef = existingSource?.SecretRef;

            if (!string.IsNullOrWhiteSpace(secretValue))
            {
                secretRef = $"system/enrichment-sources/{source.Key}";
                pendingSecretWrites.Add(
                    (
                        secretRef,
                        EnrichmentSourceCatalog.GetSecretKeyName(source.Key),
                        secretValue
                    )
                );
            }

            if (existingSource is null)
            {
                existingSource = EnrichmentSourceConfiguration.Create(
                    source.Key,
                    source.DisplayName,
                    source.Enabled,
                    secretRef,
                    source.Credentials.ApiBaseUrl,
                    source.RefreshTtlHours
                );
                await _dbContext.EnrichmentSourceConfigurations.AddAsync(existingSource, ct);
                existingSources[source.Key] = existingSource;
                if (!string.IsNullOrWhiteSpace(secretValue))
                {
                    pendingSecretAudits.Add((existingSource.Id, oldSecretRef, secretRef));
                }
                continue;
            }

            existingSource.UpdateConfiguration(
                source.DisplayName,
                source.Enabled,
                secretRef,
                source.Credentials.ApiBaseUrl,
                source.RefreshTtlHours
            );

            if (
                !string.IsNullOrWhiteSpace(secretValue)
                && !string.Equals(oldSecretRef, secretRef, StringComparison.Ordinal)
            )
            {
                pendingSecretAudits.Add((existingSource.Id, oldSecretRef, secretRef));
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        // Write secrets to vault after DB commit succeeds
        foreach (var (path, key, value) in pendingSecretWrites)
        {
            await _secretStore.PutSecretAsync(
                path,
                new Dictionary<string, string> { [key] = value },
                ct
            );
        }

        foreach (var (entityId, oldSecretRef, newSecretRef) in pendingSecretAudits)
        {
            await _auditLogWriter.WriteAsync(
                Guid.Empty,
                "EnrichmentSourceSecret",
                entityId,
                AuditAction.Updated,
                string.IsNullOrWhiteSpace(oldSecretRef) ? null : new { SecretRef = oldSecretRef },
                new { SecretRef = newSecretRef },
                ct
            );
        }

        if (pendingSecretAudits.Count > 0)
        {
            await _dbContext.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    private static EnrichmentSourceDto MapEnrichmentSourceDto(
        EnrichmentSourceConfiguration source,
        EnrichmentSourceQueueDto queue,
        IReadOnlyList<EnrichmentRunDto> recentRuns
    )
    {
        return new EnrichmentSourceDto(
            source.SourceKey,
            source.DisplayName,
            source.Enabled,
            new EnrichmentSourceCredentialsDto(
                !string.IsNullOrWhiteSpace(source.SecretRef),
                source.ApiBaseUrl
            ),
            string.Equals(
                source.SourceKey,
                EnrichmentSourceCatalog.DefenderSourceKey,
                StringComparison.OrdinalIgnoreCase
            )
                ? "tenant-source"
                : !EnrichmentSourceCatalog.RequiresCredentials(source.SourceKey)
                    ? "no-credential"
                    : "global-secret",
            source.RefreshTtlHours,
            new EnrichmentSourceRuntimeDto(
                source.LastStartedAt,
                source.LastCompletedAt,
                source.LastSucceededAt,
                source.LastStatus,
                source.LastError
            ),
            queue,
            recentRuns
        );
    }

    private static EnrichmentRunDto MapRunDto(EnrichmentRun run)
    {
        return new EnrichmentRunDto(
            run.Id,
            run.StartedAt,
            run.CompletedAt,
            run.Status.ToString(),
            run.JobsClaimed,
            run.JobsSucceeded,
            run.JobsNoData,
            run.JobsFailed,
            run.JobsRetried,
            run.LastError
        );
    }
}
