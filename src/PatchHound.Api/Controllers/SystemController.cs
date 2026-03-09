using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.System;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
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

    public SystemController(
        ISecretStore secretStore,
        PatchHoundDbContext dbContext,
        AuditLogWriter auditLogWriter
    )
    {
        _secretStore = secretStore;
        _dbContext = dbContext;
        _auditLogWriter = auditLogWriter;
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
    [Authorize(Policy = Policies.ManageUsers)]
    public async Task<ActionResult<IReadOnlyList<EnrichmentSourceDto>>> GetEnrichmentSources(
        CancellationToken ct
    )
    {
        var sources = await _dbContext
            .EnrichmentSourceConfigurations.AsNoTracking()
            .OrderBy(source => source.DisplayName)
            .ToListAsync(ct);

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
    [Authorize(Policy = Policies.ManageUsers)]
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

        var pendingSecretWrites =
            new List<(
                string Path,
                string Key,
                string Value,
                string SourceKey,
                bool HadSecret,
                string OldSecretRef
            )>();

        foreach (var source in request)
        {
            existingSources.TryGetValue(source.Key, out var existingSource);
            var secretRef = existingSource?.SecretRef ?? string.Empty;
            var secretValue = source.Credentials.Secret.Trim();

            if (!string.IsNullOrWhiteSpace(secretValue))
            {
                var hadSecret = !string.IsNullOrWhiteSpace(secretRef);
                var oldSecretRef = secretRef;
                secretRef = $"system/enrichment-sources/{source.Key}";
                pendingSecretWrites.Add(
                    (
                        secretRef,
                        EnrichmentSourceCatalog.GetSecretKeyName(source.Key),
                        secretValue,
                        source.Key,
                        hadSecret,
                        oldSecretRef
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
                    source.Credentials.ApiBaseUrl
                );
                await _dbContext.EnrichmentSourceConfigurations.AddAsync(existingSource, ct);
                existingSources[source.Key] = existingSource;
                continue;
            }

            existingSource.UpdateConfiguration(
                source.DisplayName,
                source.Enabled,
                secretRef,
                source.Credentials.ApiBaseUrl
            );
        }

        await _dbContext.SaveChangesAsync(ct);

        // Write secrets to vault after DB commit succeeds
        foreach (var (path, key, value, sourceKey, hadSecret, oldSecretRef) in pendingSecretWrites)
        {
            await _secretStore.PutSecretAsync(
                path,
                new Dictionary<string, string> { [key] = value },
                ct
            );

            if (existingSources.TryGetValue(sourceKey, out var auditSource))
            {
                await _auditLogWriter.WriteAsync(
                    Guid.Empty,
                    "EnrichmentSourceSecret",
                    auditSource.Id,
                    hadSecret ? AuditAction.Updated : AuditAction.Created,
                    hadSecret
                        ? new
                        {
                            Key = sourceKey,
                            HasSecret = true,
                            SecretRef = oldSecretRef,
                        }
                        : null,
                    new
                    {
                        Key = sourceKey,
                        HasSecret = true,
                        SecretRef = path,
                    },
                    ct
                );
            }
        }

        if (pendingSecretWrites.Count > 0)
            await _dbContext.SaveChangesAsync(ct);

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
