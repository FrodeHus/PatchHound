using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Admin;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Secrets;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly ISecretStore _secretStore;
    private readonly AuditLogWriter _auditLogWriter;
    private readonly ITenantContext _tenantContext;

    public TenantsController(
        PatchHoundDbContext dbContext,
        ISecretStore secretStore,
        AuditLogWriter auditLogWriter,
        ITenantContext tenantContext
    )
    {
        _dbContext = dbContext;
        _secretStore = secretStore;
        _auditLogWriter = auditLogWriter;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<ActionResult<PagedResponse<TenantListItemDto>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var query = _dbContext.Tenants.AsNoTracking();

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(t => t.Name)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.EntraTenantId,
            })
            .ToListAsync(ct);

        var tenantIds = items.Select(item => item.Id).ToList();
        var sourceCounts = await _dbContext
            .TenantSourceConfigurations.AsNoTracking()
            .Where(source => tenantIds.Contains(source.TenantId))
            .ToListAsync(ct);

        return Ok(
            new PagedResponse<TenantListItemDto>(
                items
                    .Select(t => new TenantListItemDto(
                        t.Id,
                        t.Name,
                        t.EntraTenantId,
                        sourceCounts.Count(source =>
                            source.TenantId == t.Id
                            && TenantSourceCatalog.HasConfiguredCredentials(source)
                        )
                    ))
                    .ToList(),
                totalCount,
                pagination.Page,
                pagination.BoundedPageSize
            )
        );
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<ActionResult<TenantDetailDto>> Get(Guid id, CancellationToken ct)
    {
        if (!_tenantContext.HasAccessToTenant(id))
            return Forbid();

        var tenant = await _dbContext
            .Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound();

        return Ok(await BuildTenantDetailDto(id, ignoreQueryFilters: false, ct));
    }

    [HttpPost]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<ActionResult<TenantDetailDto>> Create(
        [FromBody] CreateTenantRequest request,
        CancellationToken ct
    )
    {
        var name = request.Name.Trim();
        var entraTenantId = request.EntraTenantId.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return ValidationProblem("Tenant name is required.");

        if (string.IsNullOrWhiteSpace(entraTenantId))
            return ValidationProblem("Entra tenant ID is required.");

        var normalizedEntraTenantId = entraTenantId.ToLowerInvariant();
        var existingTenant = await _dbContext
            .Tenants.IgnoreQueryFilters()
            .AnyAsync(
                tenant =>
                    tenant.Name == name
                    || tenant.EntraTenantId.ToLower() == normalizedEntraTenantId,
                ct
            );

        if (existingTenant)
        {
            return Conflict(
                new ProblemDetails
                {
                    Title = "Tenant already exists",
                    Detail =
                        "A tenant with the same name or Entra tenant ID is already registered.",
                }
            );
        }

        var tenant = Tenant.Create(name, entraTenantId);
        await _dbContext.Tenants.AddAsync(tenant, ct);
        await _dbContext.Teams.AddAsync(
            Team.CreateDefault(tenant.Id, DefaultTeamHelper.DefaultTeamName),
            ct
        );
        await _dbContext.TenantSlaConfigurations.AddAsync(
            TenantSlaConfiguration.CreateDefault(tenant.Id),
            ct
        );

        foreach (var source in TenantSourceCatalog.CreateDefaults(tenant.Id))
        {
            await _dbContext.TenantSourceConfigurations.AddAsync(source, ct);
        }

        if (_tenantContext.CurrentUserId != Guid.Empty)
        {
            var sourceTenantIds = _tenantContext.CurrentTenantId is Guid currentTenantId
                ? new[] { currentTenantId }
                : _tenantContext.AccessibleTenantIds;

            var rolesToCopy = await _dbContext
                .UserTenantRoles.IgnoreQueryFilters()
                .Where(role =>
                    role.UserId == _tenantContext.CurrentUserId
                    && sourceTenantIds.Contains(role.TenantId)
                )
                .Select(role => role.Role)
                .Distinct()
                .ToListAsync(ct);

            foreach (var role in rolesToCopy)
            {
                await _dbContext.UserTenantRoles.AddAsync(
                    UserTenantRole.Create(_tenantContext.CurrentUserId, tenant.Id, role),
                    ct
                );
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        var detail = await BuildTenantDetailDto(tenant.Id, ignoreQueryFilters: true, ct);
        return CreatedAtAction(nameof(Get), new { id = tenant.Id }, detail);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateTenantRequest request,
        CancellationToken ct
    )
    {
        if (!_tenantContext.HasAccessToTenant(id))
            return Forbid();

        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Name))
            return ValidationProblem("Tenant name is required.");
        if (
            request.Sla.CriticalDays <= 0
            || request.Sla.HighDays <= 0
            || request.Sla.MediumDays <= 0
            || request.Sla.LowDays <= 0
        )
            return ValidationProblem("SLA days must be positive integers.");

        tenant.UpdateName(request.Name.Trim());
        var sla = await _dbContext.TenantSlaConfigurations.FirstOrDefaultAsync(
            config => config.TenantId == tenant.Id,
            ct
        );
        if (sla is null)
        {
            sla = TenantSlaConfiguration.CreateDefault(tenant.Id);
            await _dbContext.TenantSlaConfigurations.AddAsync(sla, ct);
        }
        sla.Update(
            request.Sla.CriticalDays,
            request.Sla.HighDays,
            request.Sla.MediumDays,
            request.Sla.LowDays
        );
        var existingSources = await _dbContext
            .TenantSourceConfigurations.Where(source => source.TenantId == tenant.Id)
            .ToDictionaryAsync(source => source.SourceKey, StringComparer.OrdinalIgnoreCase, ct);

        // Collect pending secret writes — vault writes happen after DB commit
        var pendingSecretWrites =
            new List<(
                string Path,
                string Key,
                string Value,
                Guid SourceId,
                bool HadSecret,
                string OldSecretRef,
                string SourceKey
            )>();

        foreach (var source in request.IngestionSources)
        {
            existingSources.TryGetValue(source.Key, out var existingSource);
            var secretRef = existingSource?.SecretRef ?? string.Empty;
            var secretValue = source.Credentials.Secret.Trim();

            if (string.IsNullOrWhiteSpace(secretValue))
            {
                secretValue = string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(secretValue))
            {
                var hadSecret = !string.IsNullOrWhiteSpace(secretRef);
                var oldSecretRef = secretRef;
                secretRef = $"tenants/{tenant.Id}/sources/{source.Key}";

                // Defer the actual vault write
                pendingSecretWrites.Add(
                    (
                        secretRef,
                        TenantSourceCatalog.GetSecretKeyName(source.Key),
                        secretValue,
                        existingSource?.Id ?? Guid.Empty,
                        hadSecret,
                        oldSecretRef,
                        source.Key
                    )
                );
            }

            if (existingSource is null)
            {
                var created = TenantSourceConfiguration.Create(
                    tenant.Id,
                    source.Key,
                    source.DisplayName,
                    source.Enabled,
                    source.SyncSchedule,
                    tenant.EntraTenantId,
                    source.Credentials.ClientId,
                    secretRef,
                    source.Credentials.ApiBaseUrl,
                    source.Credentials.TokenScope
                );
                await _dbContext.TenantSourceConfigurations.AddAsync(created, ct);
                existingSources[source.Key] = created;
                continue;
            }

            existingSource.UpdateConfiguration(
                source.DisplayName,
                source.Enabled,
                source.SyncSchedule,
                tenant.EntraTenantId,
                source.Credentials.ClientId,
                secretRef,
                source.Credentials.ApiBaseUrl,
                source.Credentials.TokenScope
            );
        }

        await _dbContext.SaveChangesAsync(ct);

        // Write secrets to vault after DB commit succeeds
        foreach (
            var (
                path,
                key,
                value,
                sourceId,
                hadSecret,
                oldSecretRef,
                sourceKey
            ) in pendingSecretWrites
        )
        {
            await _secretStore.PutSecretAsync(
                path,
                new Dictionary<string, string> { [key] = value },
                ct
            );

            existingSources.TryGetValue(sourceKey, out var auditSource);
            var auditEntityId = auditSource?.Id ?? sourceId;
            if (auditEntityId != Guid.Empty)
            {
                await _auditLogWriter.WriteAsync(
                    tenant.Id,
                    "TenantSourceSecret",
                    auditEntityId,
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

    [HttpPost("{id:guid}/ingestion-sources/{sourceKey}/sync")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> TriggerSync(Guid id, string sourceKey, CancellationToken ct)
    {
        if (!_tenantContext.HasAccessToTenant(id))
            return Forbid();

        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound();

        var configuredSource = await _dbContext.TenantSourceConfigurations.FirstOrDefaultAsync(
            source => source.TenantId == tenant.Id && source.SourceKey == normalizedSourceKey,
            ct
        );

        if (configuredSource is null)
        {
            return NotFound(new ProblemDetails { Title = "Ingestion source not found" });
        }

        if (!TenantSourceCatalog.SupportsManualSync(configuredSource))
        {
            return BadRequest(
                new ProblemDetails { Title = "This source does not support manual sync." }
            );
        }

        if (!configuredSource.Enabled)
        {
            return BadRequest(
                new ProblemDetails { Title = "Enable the source before triggering a manual sync." }
            );
        }

        if (!TenantSourceCatalog.HasConfiguredCredentials(configuredSource))
        {
            return BadRequest(
                new ProblemDetails
                {
                    Title = "Configure source credentials before triggering a manual sync.",
                }
            );
        }

        configuredSource.QueueManualSync(DateTimeOffset.UtcNow);
        await _dbContext.SaveChangesAsync(ct);

        return Accepted();
    }

    [HttpGet("{id:guid}/ingestion-sources/{sourceKey}/runs")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<ActionResult<PagedResponse<TenantIngestionRunDto>>> ListRuns(
        Guid id,
        string sourceKey,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        if (!_tenantContext.HasAccessToTenant(id))
            return Forbid();

        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound();

        var query = _dbContext
            .IngestionRuns.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(run => run.TenantId == id && run.SourceKey == normalizedSourceKey);

        var totalCount = await query.CountAsync(ct);
        var pagedRuns = await query
            .OrderByDescending(run => run.StartedAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToListAsync(ct);
        var runIds = pagedRuns.Select(run => run.Id).ToList();
        var snapshotsByRunId =
            runIds.Count == 0
                ? new Dictionary<Guid, IngestionSnapshot>()
                : (await _dbContext
                    .IngestionSnapshots.AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(snapshot => runIds.Contains(snapshot.IngestionRunId))
                    .OrderByDescending(snapshot => snapshot.CreatedAt)
                    .ToListAsync(ct))
                    .GroupBy(snapshot => snapshot.IngestionRunId)
                    .ToDictionary(group => group.Key, group => group.First());
        var checkpointsByRunId =
            runIds.Count == 0
                ? new Dictionary<Guid, IngestionCheckpoint>()
                : (await _dbContext
                    .IngestionCheckpoints.AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(checkpoint => runIds.Contains(checkpoint.IngestionRunId))
                    .OrderByDescending(checkpoint => checkpoint.LastCommittedAt)
                    .ToListAsync(ct))
                    .GroupBy(checkpoint => checkpoint.IngestionRunId)
                    .ToDictionary(group => group.Key, group => group.First());
        var items = pagedRuns
            .OrderByDescending(run => run.StartedAt)
            .Select(run => new TenantIngestionRunDto(
                run.Id,
                run.StartedAt,
                run.CompletedAt,
                run.Status,
                run.StagedMachineCount,
                run.StagedVulnerabilityCount,
                run.StagedSoftwareCount,
                run.PersistedMachineCount,
                run.PersistedVulnerabilityCount,
                run.PersistedSoftwareCount,
                run.Error,
                snapshotsByRunId.GetValueOrDefault(run.Id)?.Status,
                checkpointsByRunId.GetValueOrDefault(run.Id)?.Phase,
                checkpointsByRunId.GetValueOrDefault(run.Id)?.BatchNumber,
                checkpointsByRunId.GetValueOrDefault(run.Id)?.Status,
                checkpointsByRunId.GetValueOrDefault(run.Id)?.RecordsCommitted,
                checkpointsByRunId.GetValueOrDefault(run.Id)?.LastCommittedAt
            ))
            .ToList();

        return Ok(
            new PagedResponse<TenantIngestionRunDto>(
                items,
                totalCount,
                pagination.Page,
                pagination.BoundedPageSize
            )
        );
    }

    [HttpDelete("{id:guid}/ingestion-sources/{sourceKey}/runs/{runId:guid}")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> DeleteRun(
        Guid id,
        string sourceKey,
        Guid runId,
        CancellationToken ct
    )
    {
        if (!_tenantContext.HasAccessToTenant(id))
            return Forbid();

        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound();

        var source = await _dbContext.TenantSourceConfigurations.FirstOrDefaultAsync(
            item => item.TenantId == id && item.SourceKey == normalizedSourceKey,
            ct
        );

        if (source is null)
            return NotFound(new ProblemDetails { Title = "Ingestion source not found" });

        var run = await _dbContext.IngestionRuns.FirstOrDefaultAsync(
            item => item.Id == runId && item.TenantId == id && item.SourceKey == normalizedSourceKey,
            ct
        );

        if (run is null)
            return NotFound(new ProblemDetails { Title = "Ingestion run not found" });

        var isActiveRun =
            source.ActiveIngestionRunId == runId
            || (run.CompletedAt is null && IngestionRunStatePolicy.IsActive(run.Status));

        if (isActiveRun)
        {
            return Conflict(
                new ProblemDetails
                {
                    Title = "Active ingestion runs cannot be deleted",
                    Detail = "Wait for the run to finish or fail before deleting it.",
                }
            );
        }

        var checkpoints = await _dbContext
            .IngestionCheckpoints.Where(item => item.IngestionRunId == runId)
            .ToListAsync(ct);
        var stagedVulnerabilities = await _dbContext
            .StagedVulnerabilities.Where(item => item.IngestionRunId == runId)
            .ToListAsync(ct);
        var stagedVulnerabilityExposures = await _dbContext
            .StagedVulnerabilityExposures.Where(item => item.IngestionRunId == runId)
            .ToListAsync(ct);
        var stagedAssets = await _dbContext
            .StagedAssets.Where(item => item.IngestionRunId == runId)
            .ToListAsync(ct);
        var stagedInstallations = await _dbContext
            .StagedDeviceSoftwareInstallations.Where(item => item.IngestionRunId == runId)
            .ToListAsync(ct);

        _dbContext.IngestionCheckpoints.RemoveRange(checkpoints);
        _dbContext.StagedVulnerabilities.RemoveRange(stagedVulnerabilities);
        _dbContext.StagedVulnerabilityExposures.RemoveRange(stagedVulnerabilityExposures);
        _dbContext.StagedAssets.RemoveRange(stagedAssets);
        _dbContext.StagedDeviceSoftwareInstallations.RemoveRange(stagedInstallations);
        _dbContext.IngestionRuns.Remove(run);

        await _dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("{id:guid}/ingestion-sources/{sourceKey}/runs/{runId:guid}/abort")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> AbortRun(
        Guid id,
        string sourceKey,
        Guid runId,
        CancellationToken ct
    )
    {
        if (!_tenantContext.HasAccessToTenant(id))
            return Forbid();

        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound();

        var source = await _dbContext.TenantSourceConfigurations.FirstOrDefaultAsync(
            item => item.TenantId == id && item.SourceKey == normalizedSourceKey,
            ct
        );

        if (source is null)
            return NotFound(new ProblemDetails { Title = "Ingestion source not found" });

        var run = await _dbContext.IngestionRuns.FirstOrDefaultAsync(
            item => item.Id == runId && item.TenantId == id && item.SourceKey == normalizedSourceKey,
            ct
        );

        if (run is null)
            return NotFound(new ProblemDetails { Title = "Ingestion run not found" });

        if (run.CompletedAt is not null)
        {
            return Conflict(
                new ProblemDetails
                {
                    Title = "Only active ingestion runs can be aborted",
                    Detail = "This run has already completed.",
                }
            );
        }

        var now = DateTimeOffset.UtcNow;
        var abortedMessage = IngestionFailurePolicy.Describe(new IngestionAbortedException());
        run.RequestAbort(now);
        run.Abort(now, abortedMessage);
        if (source.ActiveIngestionRunId == runId)
        {
            source.ReleaseLease(runId);
            source.UpdateRuntime(
                source.ManualRequestedAt,
                source.LastStartedAt,
                now,
                source.LastSucceededAt,
                IngestionRunStatuses.FailedTerminal,
                abortedMessage
            );
        }
        await _dbContext.SaveChangesAsync(ct);

        return Accepted();
    }

    private static TenantIngestionSourceDto MapSourceDto(
        TenantSourceConfiguration source,
        IngestionCheckpoint? activeCheckpoint,
        IngestionRun? activeRun,
        IngestionSnapshot? activeSnapshot,
        IngestionSnapshot? buildingSnapshot,
        IReadOnlyList<TenantIngestionRunDto> recentRuns
    )
    {
        return new TenantIngestionSourceDto(
            source.SourceKey,
            source.DisplayName,
            source.Enabled,
            source.SyncSchedule,
            TenantSourceCatalog.SupportsScheduling(source),
            TenantSourceCatalog.SupportsManualSync(source),
            new TenantSourceCredentialsDto(
                source.ClientId,
                !string.IsNullOrWhiteSpace(source.SecretRef),
                source.ApiBaseUrl,
                source.TokenScope
            ),
            new TenantIngestionRuntimeDto(
                source.ManualRequestedAt,
                source.LastStartedAt,
                source.LastCompletedAt,
                source.LastSucceededAt,
                source.LastStatus,
                source.LastError,
                source.ActiveIngestionRunId,
                source.LeaseExpiresAt,
                activeSnapshot?.Status,
                buildingSnapshot?.Status,
                activeCheckpoint?.Phase,
                activeCheckpoint?.BatchNumber,
                activeCheckpoint?.Status,
                activeCheckpoint?.RecordsCommitted,
                activeCheckpoint?.LastCommittedAt,
                activeRun?.StagedMachineCount,
                activeRun?.StagedVulnerabilityCount,
                activeRun?.StagedSoftwareCount,
                activeRun?.PersistedMachineCount,
                activeRun?.PersistedVulnerabilityCount,
                activeRun?.PersistedSoftwareCount
            ),
            recentRuns
        );
    }

    private async Task<TenantDetailDto> BuildTenantDetailDto(
        Guid tenantId,
        bool ignoreQueryFilters,
        CancellationToken ct
    )
    {
        var tenantQuery = _dbContext.Tenants.AsNoTracking();
        if (ignoreQueryFilters)
        {
            tenantQuery = tenantQuery.IgnoreQueryFilters();
        }

        var tenant = await tenantQuery.SingleAsync(t => t.Id == tenantId, ct);

        var assetsQuery = _dbContext.Assets.AsNoTracking();
        if (ignoreQueryFilters)
        {
            assetsQuery = assetsQuery.IgnoreQueryFilters();
        }

        var assetCounts = await assetsQuery
            .Where(asset => asset.TenantId == tenantId)
            .GroupBy(asset => asset.AssetType)
            .Select(group => new { AssetType = group.Key, Count = group.Count() })
            .ToListAsync(ct);

        var assetSummary = new TenantAssetSummaryDto(
            assetCounts.Sum(item => item.Count),
            assetCounts.FirstOrDefault(item => item.AssetType == AssetType.Device)?.Count ?? 0,
            assetCounts.FirstOrDefault(item => item.AssetType == AssetType.Software)?.Count ?? 0,
            assetCounts.FirstOrDefault(item => item.AssetType == AssetType.CloudResource)?.Count
                ?? 0
        );
        var slaQuery = _dbContext.TenantSlaConfigurations.AsNoTracking();
        if (ignoreQueryFilters)
        {
            slaQuery = slaQuery.IgnoreQueryFilters();
        }

        var sla = await slaQuery.FirstOrDefaultAsync(config => config.TenantId == tenantId, ct);
        var slaDto = new TenantSlaConfigurationDto(
            sla?.CriticalDays ?? 7,
            sla?.HighDays ?? 30,
            sla?.MediumDays ?? 90,
            sla?.LowDays ?? 180
        );

        var sourcesQuery = _dbContext.TenantSourceConfigurations.AsNoTracking();
        if (ignoreQueryFilters)
        {
            sourcesQuery = sourcesQuery.IgnoreQueryFilters();
        }

        var sources = await sourcesQuery
            .Where(source => source.TenantId == tenantId)
            .OrderBy(source => source.DisplayName)
            .ToListAsync(ct);
        var sourceKeys = sources.Select(source => source.SourceKey).ToList();
        var activeRunIds = sources
            .Where(source => source.ActiveIngestionRunId.HasValue)
            .Select(source => source.ActiveIngestionRunId!.Value)
            .ToList();
        var recentRuns =
            sourceKeys.Count == 0
                ? []
                : await _dbContext
                    .IngestionRuns.AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(run => run.TenantId == tenantId && sourceKeys.Contains(run.SourceKey))
                    .OrderByDescending(run => run.StartedAt)
                    .ToListAsync(ct);
        var activeRunsById = recentRuns
            .Where(run => activeRunIds.Contains(run.Id))
            .ToDictionary(run => run.Id);
        var checkpointRunIds = recentRuns
            .Select(run => run.Id)
            .Concat(activeRunIds)
            .Distinct()
            .ToList();
        var snapshotIds = sources
            .SelectMany(source =>
                new[] { source.ActiveSnapshotId, source.BuildingSnapshotId }.Where(id => id.HasValue)
            )
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var snapshotsById =
            snapshotIds.Count == 0
                ? new Dictionary<Guid, IngestionSnapshot>()
                : await _dbContext
                    .IngestionSnapshots.AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(snapshot => snapshotIds.Contains(snapshot.Id))
                    .ToDictionaryAsync(snapshot => snapshot.Id, ct);
        var snapshotsByRunId =
            checkpointRunIds.Count == 0
                ? new Dictionary<Guid, IngestionSnapshot>()
                : await _dbContext
                    .IngestionSnapshots.AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(snapshot => checkpointRunIds.Contains(snapshot.IngestionRunId))
                    .GroupBy(snapshot => snapshot.IngestionRunId)
                    .ToDictionaryAsync(
                        group => group.Key,
                        group => group.OrderByDescending(item => item.CreatedAt).First(),
                        ct
                    );
        var latestCheckpointsByRunId =
            checkpointRunIds.Count == 0
                ? new Dictionary<Guid, IngestionCheckpoint>()
                : await _dbContext
                    .IngestionCheckpoints.AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(checkpoint => checkpointRunIds.Contains(checkpoint.IngestionRunId))
                    .OrderByDescending(checkpoint => checkpoint.LastCommittedAt)
                    .GroupBy(checkpoint => checkpoint.IngestionRunId)
                    .ToDictionaryAsync(group => group.Key, group => group.First(), ct);
        var recentRunsBySourceKey = recentRuns
            .GroupBy(run => run.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                    (IReadOnlyList<TenantIngestionRunDto>)
                        group
                            .Take(5)
                            .Select(run => new TenantIngestionRunDto(
                                run.Id,
                                run.StartedAt,
                                run.CompletedAt,
                                run.Status,
                                run.StagedMachineCount,
                                run.StagedVulnerabilityCount,
                                run.StagedSoftwareCount,
                                run.PersistedMachineCount,
                                run.PersistedVulnerabilityCount,
                                run.PersistedSoftwareCount,
                                run.Error,
                                snapshotsByRunId.GetValueOrDefault(run.Id)?.Status,
                                latestCheckpointsByRunId.GetValueOrDefault(run.Id)?.Phase,
                                latestCheckpointsByRunId.GetValueOrDefault(run.Id)?.BatchNumber,
                                latestCheckpointsByRunId.GetValueOrDefault(run.Id)?.Status,
                                latestCheckpointsByRunId.GetValueOrDefault(run.Id)?.RecordsCommitted,
                                latestCheckpointsByRunId.GetValueOrDefault(run.Id)?.LastCommittedAt
                            ))
                            .ToList(),
                StringComparer.OrdinalIgnoreCase
            );

        return new TenantDetailDto(
            tenant.Id,
            tenant.Name,
            tenant.EntraTenantId,
            assetSummary,
            slaDto,
            sources
                .Select(source =>
                    MapSourceDto(
                        source,
                        source.ActiveIngestionRunId.HasValue
                            ? latestCheckpointsByRunId.GetValueOrDefault(source.ActiveIngestionRunId.Value)
                            : null,
                        source.ActiveIngestionRunId.HasValue
                            ? activeRunsById.GetValueOrDefault(source.ActiveIngestionRunId.Value)
                            : null,
                        source.ActiveSnapshotId.HasValue
                            ? snapshotsById.GetValueOrDefault(source.ActiveSnapshotId.Value)
                            : null,
                        source.BuildingSnapshotId.HasValue
                            ? snapshotsById.GetValueOrDefault(source.BuildingSnapshotId.Value)
                            : null,
                        recentRunsBySourceKey.GetValueOrDefault(source.SourceKey, [])
                    )
                )
                .ToList()
        );
    }

    [HttpGet("{id:guid}/ingestion-sources/{sourceKey}/runs/{runId:guid}/progress")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<ActionResult<TenantIngestionRunDto>> GetRunProgress(
        Guid id,
        string sourceKey,
        Guid runId,
        CancellationToken ct
    )
    {
        if (!_tenantContext.HasAccessToTenant(id))
            return Forbid();

        var normalizedSourceKey = sourceKey.Trim().ToLowerInvariant();
        var run = await _dbContext
            .IngestionRuns.AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                item => item.Id == runId && item.TenantId == id && item.SourceKey == normalizedSourceKey,
                ct
            );

        if (run is null)
            return NotFound();

        var snapshot = await _dbContext
            .IngestionSnapshots.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == runId)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var checkpoint = await _dbContext
            .IngestionCheckpoints.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.IngestionRunId == runId)
            .OrderByDescending(item => item.LastCommittedAt)
            .FirstOrDefaultAsync(ct);

        return Ok(
            new TenantIngestionRunDto(
                run.Id,
                run.StartedAt,
                run.CompletedAt,
                run.Status,
                run.StagedMachineCount,
                run.StagedVulnerabilityCount,
                run.StagedSoftwareCount,
                run.PersistedMachineCount,
                run.PersistedVulnerabilityCount,
                run.PersistedSoftwareCount,
                run.Error,
                snapshot?.Status,
                checkpoint?.Phase,
                checkpoint?.BatchNumber,
                checkpoint?.Status,
                checkpoint?.RecordsCommitted,
                checkpoint?.LastCommittedAt
            )
        );
    }
}
