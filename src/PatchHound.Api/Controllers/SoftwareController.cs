using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Software;
using PatchHound.Api.Services;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/software")]
[Authorize]
public class SoftwareController(
    PatchHoundDbContext dbContext,
    TenantAiTextGenerationService tenantAiTextGenerationService,
    SoftwareDescriptionJobService softwareDescriptionJobService,
    ITenantAiConfigurationResolver tenantAiConfigurationResolver,
    ITenantAiResearchService tenantAiResearchService,
    RemediationTaskQueryService remediationTaskQueryService,
    CycloneDxSupplyChainImportService cycloneDxSupplyChainImportService,
    ITenantContext tenantContext
) : ControllerBase
{
    private sealed record VulnerabilityEvidenceRow(
        string Method,
        string Confidence,
        string Evidence,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset LastSeenAt,
        DateTimeOffset? ResolvedAt
    );

    private sealed record ExposureImpactVulnerabilityRow(
        string ExternalId,
        Severity Severity,
        decimal? CvssScore
    );

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<TenantSoftwareDetailDto>> Get(
        Guid id,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid currentTenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var activeSnapshotId = await ResolveActiveSoftwareSnapshotIdAsync(currentTenantId, ct);

        var tenantSoftware = await dbContext
            .TenantSoftware.AsNoTracking()
            .Where(item =>
                item.Id == id
                && item.TenantId == currentTenantId
                && item.SnapshotId == activeSnapshotId
            )
            .Select(item => new
            {
                item.Id,
                item.TenantId,
                item.NormalizedSoftwareId,
                item.FirstSeenAt,
                item.LastSeenAt,
                item.NormalizedSoftware.CanonicalName,
                item.NormalizedSoftware.CanonicalVendor,
                item.NormalizedSoftware.Category,
                item.NormalizedSoftware.Description,
                item.NormalizedSoftware.DescriptionGeneratedAt,
                item.NormalizedSoftware.DescriptionProviderType,
                item.NormalizedSoftware.DescriptionProfileName,
                item.NormalizedSoftware.DescriptionModel,
                item.NormalizedSoftware.EolProductSlug,
                item.NormalizedSoftware.EolDate,
                item.NormalizedSoftware.EolLatestVersion,
                item.NormalizedSoftware.EolIsLts,
                item.NormalizedSoftware.EolSupportEndDate,
                item.NormalizedSoftware.EolIsDiscontinued,
                item.NormalizedSoftware.EolEnrichedAt,
                item.NormalizedSoftware.SupplyChainRemediationPath,
                item.NormalizedSoftware.SupplyChainInsightConfidence,
                item.NormalizedSoftware.SupplyChainSourceFormat,
                item.NormalizedSoftware.SupplyChainPrimaryComponentName,
                item.NormalizedSoftware.SupplyChainPrimaryComponentVersion,
                item.NormalizedSoftware.SupplyChainFixedVersion,
                item.NormalizedSoftware.SupplyChainAffectedVulnerabilityCount,
                item.NormalizedSoftware.SupplyChainSummary,
                item.NormalizedSoftware.SupplyChainEnrichedAt,
            })
            .FirstOrDefaultAsync(ct);
        if (tenantSoftware is null)
        {
            return NotFound();
        }

        var installations = await dbContext
            .NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item => item.TenantSoftwareId == id && item.SnapshotId == activeSnapshotId)
            .ToListAsync(ct);

        var activeInstallations = installations.Where(item => item.IsActive).ToList();
        var softwareAssetIds = installations.Select(item => item.SoftwareAssetId).Distinct().ToList();

        var openMatches = await dbContext
            .SoftwareVulnerabilityMatches.AsNoTracking()
            .Where(match =>
                match.SnapshotId == activeSnapshotId
                && match.ResolvedAt == null
                && softwareAssetIds.Contains(match.SoftwareAssetId)
            )
            .Select(match => new { match.SoftwareAssetId, match.VulnerabilityDefinitionId })
            .Distinct()
            .ToListAsync(ct);
        var openMatchSoftwareAssetIds = openMatches
            .Select(item => item.SoftwareAssetId)
            .ToHashSet();

        var versionCohorts = activeInstallations
            .GroupBy(item => NormalizeVersionKey(item.DetectedVersion))
            .Select(group =>
            {
                var groupSoftwareAssetIds = group.Select(item => item.SoftwareAssetId).ToHashSet();
                return new TenantSoftwareVersionCohortDto(
                    RestoreVersion(group.Key),
                    group.Count(),
                    group.Select(item => item.DeviceAssetId).Distinct().Count(),
                    openMatches.Count(item => groupSoftwareAssetIds.Contains(item.SoftwareAssetId)),
                    group.Min(item => item.FirstSeenAt),
                    group.Max(item => item.LastSeenAt)
                );
            })
            .OrderByDescending(item => item.ActiveInstallCount)
            .ThenByDescending(item => item.ActiveVulnerabilityCount)
            .ThenBy(item => item.Version ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var openVulnerabilityRows = await dbContext
            .NormalizedSoftwareVulnerabilityProjections.AsNoTracking()
            .Where(item =>
                item.TenantSoftwareId == id
                && item.SnapshotId == activeSnapshotId
                && item.ResolvedAt == null
            )
            .Select(item => new ExposureImpactVulnerabilityRow(
                item.VulnerabilityDefinition.ExternalId,
                item.VulnerabilityDefinition.VendorSeverity,
                item.VulnerabilityDefinition.CvssScore
            ))
            .ToListAsync(ct);

        var activeDeviceIds = activeInstallations
            .Select(item => item.DeviceAssetId)
            .Distinct()
            .ToList();
        var highValueDeviceCount = activeDeviceIds.Count == 0
            ? 0
            : await dbContext.Assets.AsNoTracking()
                .Where(asset => activeDeviceIds.Contains(asset.Id))
                .CountAsync(asset =>
                    asset.Criticality == Criticality.High || asset.Criticality == Criticality.Critical,
                    ct
                );
        var impactBreakdown = BuildExposureImpactBreakdown(
            id,
            activeDeviceIds.Count,
            highValueDeviceCount,
            openVulnerabilityRows
        );

        var remediationSummary = await remediationTaskQueryService.BuildSoftwareSummaryAsync(
            currentTenantId,
            id,
            ct
        );

        return Ok(
            new TenantSoftwareDetailDto(
                tenantSoftware.Id,
                tenantSoftware.NormalizedSoftwareId,
                softwareAssetIds.FirstOrDefault() is var primaryAssetId && primaryAssetId != Guid.Empty ? primaryAssetId : null,
                tenantSoftware.CanonicalName,
                tenantSoftware.CanonicalVendor,
                tenantSoftware.Category,
                tenantSoftware.Description,
                tenantSoftware.DescriptionGeneratedAt,
                tenantSoftware.DescriptionProviderType,
                tenantSoftware.DescriptionProfileName,
                tenantSoftware.DescriptionModel,
                installations.Count == 0 ? tenantSoftware.FirstSeenAt : installations.Min(item => item.FirstSeenAt),
                installations.Count == 0 ? tenantSoftware.LastSeenAt : installations.Max(item => item.LastSeenAt),
                activeInstallations.Count,
                activeInstallations.Select(item => item.DeviceAssetId).Distinct().Count(),
                activeInstallations.Count(item =>
                    openMatchSoftwareAssetIds.Contains(item.SoftwareAssetId)
                ),
                openVulnerabilityRows.Count,
                versionCohorts.Count,
                impactBreakdown.ImpactScore,
                ToExposureImpactExplanationDto(
                    activeDeviceIds.Count,
                    highValueDeviceCount,
                    impactBreakdown
                ),
                remediationSummary,
                versionCohorts,
                tenantSoftware.EolEnrichedAt.HasValue
                    ? new SoftwareLifecycleDto(
                        tenantSoftware.EolDate,
                        tenantSoftware.EolLatestVersion,
                        tenantSoftware.EolIsLts,
                        tenantSoftware.EolSupportEndDate,
                        tenantSoftware.EolIsDiscontinued,
                        tenantSoftware.EolEnrichedAt,
                        tenantSoftware.EolProductSlug
                    )
                    : null,
                tenantSoftware.SupplyChainEnrichedAt.HasValue
                    ? new SupplyChainInsightDto(
                        tenantSoftware.SupplyChainRemediationPath.ToString(),
                        tenantSoftware.SupplyChainInsightConfidence.ToString(),
                        tenantSoftware.SupplyChainSourceFormat,
                        tenantSoftware.SupplyChainPrimaryComponentName,
                        tenantSoftware.SupplyChainPrimaryComponentVersion,
                        tenantSoftware.SupplyChainFixedVersion,
                        tenantSoftware.SupplyChainAffectedVulnerabilityCount,
                        tenantSoftware.SupplyChainSummary ?? string.Empty,
                        tenantSoftware.SupplyChainEnrichedAt
                    )
                    : null
            )
        );
    }

    [HttpPost("{id:guid}/supply-chain/cyclonedx")]
    [Authorize(Policy = Policies.ModifyVulnerabilities)]
    public async Task<ActionResult<SupplyChainInsightDto>> ImportCycloneDxEvidence(
        Guid id,
        [FromBody] ImportTenantSoftwareSupplyChainRequest request,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid currentTenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var tenantSoftware = await dbContext.TenantSoftware.FirstOrDefaultAsync(
            item => item.Id == id && item.TenantId == currentTenantId,
            ct
        );
        if (tenantSoftware is null)
        {
            return NotFound();
        }

        var result = await cycloneDxSupplyChainImportService.ImportAsync(
            tenantSoftware.Id,
            request.DocumentJson,
            ct
        );

        return Ok(
            new SupplyChainInsightDto(
                result.RemediationPath.ToString(),
                result.Confidence.ToString(),
                result.SourceFormat,
                result.PrimaryComponentName,
                result.PrimaryComponentVersion,
                result.FixedVersion,
                result.AffectedVulnerabilityCount,
                result.Summary,
                DateTimeOffset.UtcNow
            )
        );
    }

    [HttpPost("{id:guid}/description")]
    [Authorize(Policy = Policies.GenerateAiReports)]
    public async Task<ActionResult<TenantSoftwareDescriptionJobDto>> GenerateDescription(
        Guid id,
        [FromBody] GenerateTenantSoftwareDescriptionRequest request,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid currentTenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var activeSnapshotId = await ResolveActiveSoftwareSnapshotIdAsync(currentTenantId, ct);
        var tenantSoftwareExists = await dbContext
            .TenantSoftware.AsNoTracking()
            .AnyAsync(
                item =>
                    item.Id == id
                    && item.TenantId == currentTenantId
                    && item.SnapshotId == activeSnapshotId,
                ct
            );
        if (!tenantSoftwareExists)
        {
            return NotFound(new ProblemDetails { Title = "Tenant software not found" });
        }

        var result = await softwareDescriptionJobService.EnqueueAsync(
            currentTenantId,
            id,
            request.TenantAiProfileId,
            ct
        );
        if (!result.IsSuccess)
        {
            return BadRequest(new ProblemDetails { Title = result.Error ?? "Failed to queue software description generation." });
        }

        return Ok(
            new TenantSoftwareDescriptionJobDto(
                result.Value.Id,
                result.Value.TenantSoftwareId,
                result.Value.Status.ToString(),
                string.IsNullOrWhiteSpace(result.Value.Error) ? null : result.Value.Error,
                result.Value.RequestedAt,
                result.Value.StartedAt,
                result.Value.CompletedAt
            )
        );
    }

    [HttpGet("{id:guid}/description-status")]
    [Authorize(Policy = Policies.GenerateAiReports)]
    public async Task<ActionResult<TenantSoftwareDescriptionJobDto?>> GetDescriptionStatus(
        Guid id,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid currentTenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var job = await softwareDescriptionJobService.GetLatestAsync(currentTenantId, id, ct);
        if (job is null)
        {
            return Ok(null);
        }

        return Ok(
            new TenantSoftwareDescriptionJobDto(
                job.Id,
                job.TenantSoftwareId,
                job.Status.ToString(),
                string.IsNullOrWhiteSpace(job.Error) ? null : job.Error,
                job.RequestedAt,
                job.StartedAt,
                job.CompletedAt
            )
        );
    }

    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<TenantSoftwareListItemDto>>> List(
        [FromQuery] TenantSoftwareFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid currentTenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var query = dbContext
            .TenantSoftware.AsNoTracking()
            .Where(item => item.TenantId == currentTenantId);

        var activeSnapshotId = await ResolveActiveSoftwareSnapshotIdAsync(currentTenantId, ct);
        query = query.Where(item => item.SnapshotId == activeSnapshotId);
        query = query.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query = query.Where(item =>
                item.NormalizedSoftware.CanonicalName.Contains(filter.Search)
                || (
                    item.NormalizedSoftware.CanonicalVendor != null
                    && item.NormalizedSoftware.CanonicalVendor.Contains(filter.Search)
                )
            );
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            query = query.Where(item =>
                item.NormalizedSoftware.Category == filter.Category
            );
        }

        if (filter.VulnerableOnly == true)
        {
            query = query.Where(item =>
                dbContext.NormalizedSoftwareVulnerabilityProjections.Any(projection =>
                    projection.TenantSoftwareId == item.Id
                    && projection.SnapshotId == activeSnapshotId
                    && projection.ResolvedAt == null
                )
            );
        }

        if (filter.MissedMaintenanceWindow == true)
        {
            var now = DateTimeOffset.UtcNow;
            query = query.Where(item =>
                dbContext.RemediationDecisions.Any(decision =>
                    decision.TenantId == currentTenantId
                    && decision.TenantSoftwareId == item.Id
                    && decision.MaintenanceWindowDate != null
                    && decision.MaintenanceWindowDate < now
                    && decision.ApprovalStatus != DecisionApprovalStatus.Rejected
                    && decision.ApprovalStatus != DecisionApprovalStatus.Expired)
                && dbContext.NormalizedSoftwareVulnerabilityProjections.Any(projection =>
                    projection.TenantSoftwareId == item.Id
                    && projection.SnapshotId == activeSnapshotId
                    && projection.ResolvedAt == null
                )
            );
        }

        var totalCount = await query.CountAsync(ct);
        var rows = await query
            .Select(item => new
            {
                item.Id,
                item.NormalizedSoftwareId,
                CanonicalName = item.NormalizedSoftware.CanonicalName,
                CanonicalVendor = item.NormalizedSoftware.CanonicalVendor,
                Category = item.NormalizedSoftware.Category,
                CurrentRiskScore = dbContext.TenantSoftwareRiskScores
                    .Where(score =>
                        score.TenantSoftwareId == item.Id && score.SnapshotId == activeSnapshotId
                    )
                    .Select(score => (decimal?)score.OverallScore)
                    .FirstOrDefault(),
                ActiveInstallCount = dbContext
                    .NormalizedSoftwareInstallations
                    .Where(installation =>
                        installation.TenantSoftwareId == item.Id
                        && installation.SnapshotId == activeSnapshotId
                        && installation.IsActive
                    )
                    .Count(),
                UniqueDeviceCount = dbContext
                    .NormalizedSoftwareInstallations
                    .Where(installation =>
                        installation.TenantSoftwareId == item.Id
                        && installation.SnapshotId == activeSnapshotId
                        && installation.IsActive
                    )
                    .Select(installation => installation.DeviceAssetId)
                    .Distinct()
                    .Count(),
                ActiveVulnerabilityCount = dbContext
                    .NormalizedSoftwareVulnerabilityProjections
                    .Where(projection =>
                        projection.TenantSoftwareId == item.Id
                        && projection.SnapshotId == activeSnapshotId
                        && projection.ResolvedAt == null
                    )
                    .Count(),
                VersionCount = dbContext
                    .NormalizedSoftwareInstallations
                    .Where(installation =>
                        installation.TenantSoftwareId == item.Id
                        && installation.SnapshotId == activeSnapshotId
                        && installation.IsActive
                    )
                    .Select(installation => installation.DetectedVersion ?? string.Empty)
                    .Distinct()
                    .Count(version => version != string.Empty),
                LastSeenAt = dbContext
                    .NormalizedSoftwareInstallations
                    .Where(installation =>
                        installation.TenantSoftwareId == item.Id
                        && installation.SnapshotId == activeSnapshotId
                    )
                    .Select(installation => (DateTimeOffset?)installation.LastSeenAt)
                    .Max(),
                MaintenanceWindowDate = dbContext.RemediationDecisions
                    .Where(decision =>
                        decision.TenantId == currentTenantId
                        && decision.TenantSoftwareId == item.Id
                        && decision.ApprovalStatus != DecisionApprovalStatus.Rejected
                        && decision.ApprovalStatus != DecisionApprovalStatus.Expired)
                    .OrderByDescending(decision => decision.DecidedAt)
                    .Select(decision => decision.MaintenanceWindowDate)
                    .FirstOrDefault(),
                ExposureImpactScore = dbContext
                    .NormalizedSoftwareInstallations
                    .Where(installation =>
                        installation.TenantSoftwareId == item.Id
                        && installation.SnapshotId == activeSnapshotId
                        && installation.IsActive
                    )
                    .Select(installation => installation.SoftwareAsset.ExposureImpactScore)
                    .Max(),
            })
            .OrderByDescending(item => item.CurrentRiskScore ?? 0m)
            .ThenByDescending(item => item.ActiveVulnerabilityCount)
            .ThenByDescending(item => item.ActiveInstallCount)
            .ThenBy(item => item.CanonicalName)
            .ThenBy(item => item.Id)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToListAsync(ct);

        return Ok(
            new PagedResponse<TenantSoftwareListItemDto>(
                rows
                    .Select(item => new TenantSoftwareListItemDto(
                    item.Id,
                    item.NormalizedSoftwareId,
                    item.CanonicalName,
                    item.CanonicalVendor,
                    item.Category,
                    item.CurrentRiskScore,
                    item.ActiveInstallCount,
                        item.UniqueDeviceCount,
                        item.ActiveVulnerabilityCount,
                        item.VersionCount,
                        item.ExposureImpactScore,
                        item.LastSeenAt,
                        item.MaintenanceWindowDate
                    ))
                    .ToList(),
                totalCount,
                pagination.Page,
                pagination.BoundedPageSize
            )
        );
    }

    [HttpGet("{id:guid}/installations")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<TenantSoftwareInstallationDto>>> GetInstallations(
        Guid id,
        [FromQuery] TenantSoftwareInstallationQuery query,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid currentTenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var activeSnapshotId = await ResolveActiveSoftwareSnapshotIdAsync(currentTenantId, ct);

        var tenantSoftware = await dbContext
            .TenantSoftware.AsNoTracking()
            .Where(item =>
                item.Id == id
                && item.TenantId == currentTenantId
                && item.SnapshotId == activeSnapshotId
            )
            .Select(item => new { item.Id, item.TenantId })
            .FirstOrDefaultAsync(ct);
        if (tenantSoftware is null)
        {
            return NotFound();
        }
        var installationsQuery = dbContext
            .NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item => item.TenantSoftwareId == id && item.SnapshotId == activeSnapshotId);

        if (query.ActiveOnly)
        {
            installationsQuery = installationsQuery.Where(item => item.IsActive);
        }

        if (query.Version is not null)
        {
            if (string.IsNullOrWhiteSpace(query.Version))
            {
                installationsQuery = installationsQuery.Where(item =>
                    string.IsNullOrWhiteSpace(item.DetectedVersion)
                );
            }
            else
            {
                var version = query.Version.Trim();
                installationsQuery = installationsQuery.Where(item => item.DetectedVersion == version);
            }
        }

        var totalCount = await installationsQuery.CountAsync(ct);
        var rows = await installationsQuery
            .OrderByDescending(item => item.LastSeenAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(item => new
            {
                item.DeviceAssetId,
                DeviceName = item.DeviceAsset.DeviceComputerDnsName ?? item.DeviceAsset.Name,
                DeviceCriticality = item.DeviceAsset.Criticality.ToString(),
                item.SoftwareAssetId,
                SoftwareAssetName = item.SoftwareAsset.Name,
                item.DetectedVersion,
                item.FirstSeenAt,
                item.LastSeenAt,
                item.RemovedAt,
                item.IsActive,
                item.CurrentEpisodeNumber,
                item.DeviceAsset.OwnerUserId,
                item.DeviceAsset.OwnerTeamId,
                OwnerUserName = dbContext.Users
                    .Where(user => user.Id == item.DeviceAsset.OwnerUserId)
                    .Select(user => user.DisplayName)
                    .FirstOrDefault(),
                OwnerTeamName = dbContext.Teams
                    .Where(team => team.Id == item.DeviceAsset.OwnerTeamId)
                    .Select(team => team.Name)
                    .FirstOrDefault(),
                SecurityProfileName = dbContext
                    .AssetSecurityProfiles.Where(profile =>
                        profile.Id == item.DeviceAsset.SecurityProfileId
                    )
                    .Select(profile => profile.Name)
                    .FirstOrDefault(),
                OpenVulnerabilityCount = dbContext
                    .VulnerabilityAssets.Where(link =>
                        link.AssetId == item.DeviceAssetId
                        && link.Status == Core.Enums.VulnerabilityStatus.Open
                    )
                    .Count(),
            })
            .ToListAsync(ct);

        return Ok(
            new PagedResponse<TenantSoftwareInstallationDto>(
                rows
                    .Select(item => new TenantSoftwareInstallationDto(
                        id,
                        item.DeviceAssetId,
                        item.DeviceName,
                        item.DeviceCriticality,
                        item.SoftwareAssetId,
                        item.SoftwareAssetName,
                        item.DetectedVersion,
                        item.FirstSeenAt,
                        item.LastSeenAt,
                        item.RemovedAt,
                        item.IsActive,
                        item.CurrentEpisodeNumber,
                        item.SecurityProfileName,
                        item.OwnerUserId,
                        item.OwnerUserName,
                        item.OwnerTeamId,
                        item.OwnerTeamName,
                        item.OpenVulnerabilityCount
                    ))
                    .ToList(),
                totalCount,
                pagination.Page,
                pagination.BoundedPageSize
            )
        );
    }

    [HttpGet("{id:guid}/vulnerabilities")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<IReadOnlyList<TenantSoftwareVulnerabilityDto>>> GetVulnerabilities(
        Guid id,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid currentTenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var activeSnapshotId = await ResolveActiveSoftwareSnapshotIdAsync(currentTenantId, ct);

        var tenantSoftware = await dbContext
            .TenantSoftware.AsNoTracking()
            .Where(item =>
                item.Id == id
                && item.TenantId == currentTenantId
                && item.SnapshotId == activeSnapshotId
            )
            .Select(item => new { item.Id, item.TenantId })
            .FirstOrDefaultAsync(ct);
        if (tenantSoftware is null)
        {
            return NotFound();
        }
        var tenantVulnerabilityIdsByDefinitionId = await dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(item => item.TenantId == tenantSoftware.TenantId)
            .ToDictionaryAsync(item => item.VulnerabilityDefinitionId, item => item.Id, ct);

        var activeInstallations = await dbContext
            .NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item =>
                item.TenantSoftwareId == id && item.SnapshotId == activeSnapshotId && item.IsActive
            )
            .ToListAsync(ct);
        var relevantSoftwareAssetIds = activeInstallations
            .Select(item => item.SoftwareAssetId)
            .Distinct()
            .ToList();

        var openMatches = await dbContext
            .SoftwareVulnerabilityMatches.AsNoTracking()
            .Where(match =>
                match.SnapshotId == activeSnapshotId
                && relevantSoftwareAssetIds.Contains(match.SoftwareAssetId)
            )
            .Select(match => new
            {
                match.SoftwareAssetId,
                match.VulnerabilityDefinitionId,
                match.ResolvedAt,
                match.MatchMethod,
                match.Confidence,
                match.Evidence,
                match.FirstSeenAt,
                match.LastSeenAt,
            })
            .ToListAsync(ct);

        var vulnerabilities = await dbContext
            .VulnerabilityDefinitions.AsNoTracking()
            .Where(item =>
                openMatches.Select(match => match.VulnerabilityDefinitionId).Contains(item.Id)
            )
            .OrderByDescending(item => item.CvssScore)
            .ThenByDescending(item => item.PublishedDate)
            .ToListAsync(ct);

        return Ok(
            vulnerabilities
                .Select(vulnerability =>
                {
                    var relatedSoftwareAssetIds = openMatches
                        .Where(match => match.VulnerabilityDefinitionId == vulnerability.Id)
                        .Where(match => match.ResolvedAt is null)
                        .Select(match => match.SoftwareAssetId)
                        .ToHashSet();
                    if (relatedSoftwareAssetIds.Count == 0)
                    {
                        return null;
                    }

                    var affectedVersions = activeInstallations
                        .Where(installation =>
                            relatedSoftwareAssetIds.Contains(installation.SoftwareAssetId)
                        )
                        .Select(installation => installation.DetectedVersion)
                        .Where(version => !string.IsNullOrWhiteSpace(version))
                        .Cast<string>()
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(version => version, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (!tenantVulnerabilityIdsByDefinitionId.TryGetValue(vulnerability.Id, out var tenantVulnerabilityId))
                    {
                        return null;
                    }

                    var relatedMatches = openMatches
                        .Where(match =>
                            match.VulnerabilityDefinitionId == vulnerability.Id && match.ResolvedAt is null
                        )
                        .ToList();
                    var bestMatch = relatedMatches
                        .OrderByDescending(match => GetMethodPriority(match.MatchMethod))
                        .ThenByDescending(match => GetConfidencePriority(match.Confidence))
                        .ThenByDescending(match => match.LastSeenAt)
                        .First();

                    return new TenantSoftwareVulnerabilityDto(
                        tenantVulnerabilityId,
                        vulnerability.Id,
                        vulnerability.ExternalId,
                        vulnerability.Title,
                        vulnerability.Description,
                        vulnerability.VendorSeverity.ToString(),
                        vulnerability.CvssScore,
                        vulnerability.PublishedDate,
                        vulnerability.Source,
                        bestMatch.MatchMethod.ToString(),
                        bestMatch.Confidence.ToString(),
                        relatedMatches.Select(match => match.SoftwareAssetId).Distinct().Count(),
                        activeInstallations
                            .Where(installation =>
                                relatedSoftwareAssetIds.Contains(installation.SoftwareAssetId)
                            )
                            .Select(installation => installation.DeviceAssetId)
                            .Distinct()
                            .Count(),
                        affectedVersions.Count,
                        affectedVersions,
                        relatedMatches.Min(match => match.FirstSeenAt),
                        relatedMatches.Max(match => match.LastSeenAt),
                        null,
                        relatedMatches
                            .OrderByDescending(match => GetMethodPriority(match.MatchMethod))
                            .ThenByDescending(match => GetConfidencePriority(match.Confidence))
                            .ThenByDescending(match => match.LastSeenAt)
                            .Select(match =>
                                new TenantSoftwareVulnerabilityEvidenceDto(
                                    match.MatchMethod.ToString(),
                                    match.Confidence.ToString(),
                                    match.Evidence,
                                    match.FirstSeenAt,
                                    match.LastSeenAt,
                                    match.ResolvedAt
                                )
                            )
                            .ToList()
                    );
                })
                .Where(item => item is not null)
                .Cast<TenantSoftwareVulnerabilityDto>()
                .OrderByDescending(item => item.CvssScore ?? decimal.MinusOne)
                .ThenByDescending(item => item.PublishedDate)
                .ThenBy(item => item.ExternalId, StringComparer.OrdinalIgnoreCase)
                .ToList()
        );
    }

    [HttpPost("{id:guid}/ai-report")]
    [Authorize(Policy = Policies.GenerateAiReports)]
    public async Task<ActionResult<TenantSoftwareAiReportDto>> GenerateAiReport(
        Guid id,
        [FromBody] GenerateTenantSoftwareAiReportRequest request,
        CancellationToken ct
    )
    {
        if (tenantContext.CurrentTenantId is not Guid currentTenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var activeSnapshotId = await ResolveActiveSoftwareSnapshotIdAsync(currentTenantId, ct);

        var tenantSoftware = await dbContext
            .TenantSoftware.AsNoTracking()
            .Where(item =>
                item.Id == id
                && item.TenantId == currentTenantId
                && item.SnapshotId == activeSnapshotId
            )
            .Select(item => new
            {
                item.Id,
                item.TenantId,
                item.NormalizedSoftwareId,
                item.FirstSeenAt,
                item.LastSeenAt,
                item.NormalizedSoftware.CanonicalName,
                item.NormalizedSoftware.CanonicalVendor,
                item.NormalizedSoftware.PrimaryCpe23Uri,
                NormalizationMethod = item.NormalizedSoftware.NormalizationMethod.ToString(),
                Confidence = item.NormalizedSoftware.Confidence.ToString(),
            })
            .FirstOrDefaultAsync(ct);
        if (tenantSoftware is null)
        {
            return NotFound(new ProblemDetails { Title = "Tenant software not found" });
        }

        var aliases = await dbContext
            .NormalizedSoftwareAliases.AsNoTracking()
            .Where(item => item.NormalizedSoftwareId == tenantSoftware.NormalizedSoftwareId)
            .OrderBy(item => item.SourceSystem)
            .ThenBy(item => item.ExternalSoftwareId)
            .Select(item => new
            {
                SourceSystem = item.SourceSystem.ToString(),
                item.ExternalSoftwareId,
                item.RawName,
                item.RawVendor,
                item.RawVersion,
                AliasConfidence = item.AliasConfidence.ToString(),
                item.MatchReason,
            })
            .ToListAsync(ct);
        var installations = await dbContext
            .NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item => item.TenantSoftwareId == id && item.SnapshotId == activeSnapshotId)
            .Select(item => new
            {
                item.DeviceAssetId,
                DeviceName = item.DeviceAsset.DeviceComputerDnsName ?? item.DeviceAsset.Name,
                DeviceCriticality = item.DeviceAsset.Criticality.ToString(),
                item.SoftwareAssetId,
                SoftwareAssetName = item.SoftwareAsset.Name,
                item.DetectedVersion,
                item.FirstSeenAt,
                item.LastSeenAt,
                item.RemovedAt,
                item.IsActive,
                item.CurrentEpisodeNumber,
            })
            .ToListAsync(ct);

        var tenantVulnerabilityIdsByDefinitionId = await dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(item => item.TenantId == tenantSoftware.TenantId)
            .ToDictionaryAsync(item => item.VulnerabilityDefinitionId, item => item.Id, ct);

        var activeInstallations = installations.Where(item => item.IsActive).ToList();
        var relevantSoftwareAssetIds = activeInstallations
            .Select(item => item.SoftwareAssetId)
            .Distinct()
            .ToList();
        var uniqueDeviceCount = activeInstallations.Select(item => item.DeviceAssetId).Distinct().Count();

        var matches = await dbContext
            .SoftwareVulnerabilityMatches.AsNoTracking()
            .Where(match =>
                match.SnapshotId == activeSnapshotId
                && relevantSoftwareAssetIds.Contains(match.SoftwareAssetId)
            )
            .Select(match => new
            {
                match.SoftwareAssetId,
                match.VulnerabilityDefinitionId,
                match.ResolvedAt,
                match.MatchMethod,
                match.Confidence,
                Method = match.MatchMethod.ToString(),
                ConfidenceLabel = match.Confidence.ToString(),
                match.Evidence,
                match.FirstSeenAt,
                match.LastSeenAt,
            })
            .ToListAsync(ct);

        var vulnerabilities = await dbContext
            .VulnerabilityDefinitions.AsNoTracking()
            .Where(item => matches.Select(match => match.VulnerabilityDefinitionId).Contains(item.Id))
            .OrderByDescending(item => item.CvssScore)
            .ThenByDescending(item => item.PublishedDate)
            .ToListAsync(ct);

        var vulnerabilityPayload = vulnerabilities.Select(vulnerability =>
        {
            var relatedMatches = matches
                .Where(match => match.VulnerabilityDefinitionId == vulnerability.Id && match.ResolvedAt is null)
                .ToList();
            if (relatedMatches.Count == 0)
            {
                return null;
            }

            var relatedSoftwareAssetIds = relatedMatches.Select(match => match.SoftwareAssetId).ToHashSet();
            var affectedVersions = activeInstallations
                .Where(installation => relatedSoftwareAssetIds.Contains(installation.SoftwareAssetId))
                .Select(installation => installation.DetectedVersion)
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(version => version, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!tenantVulnerabilityIdsByDefinitionId.TryGetValue(vulnerability.Id, out var tenantVulnerabilityId))
            {
                return null;
            }

            var bestMatch = relatedMatches
                .OrderByDescending(match => GetMethodPriority(match.MatchMethod))
                .ThenByDescending(match => GetConfidencePriority(match.Confidence))
                .ThenByDescending(match => match.LastSeenAt)
                .First();

            return new
            {
                TenantVulnerabilityId = tenantVulnerabilityId,
                vulnerability.Id,
                vulnerability.ExternalId,
                vulnerability.Title,
                VendorSeverity = vulnerability.VendorSeverity.ToString(),
                vulnerability.CvssScore,
                vulnerability.PublishedDate,
                vulnerability.Source,
                BestMatchMethod = bestMatch.MatchMethod.ToString(),
                BestConfidence = bestMatch.Confidence.ToString(),
                AffectedInstallCount = relatedMatches.Select(match => match.SoftwareAssetId).Distinct().Count(),
                AffectedDeviceCount = activeInstallations
                    .Where(installation => relatedSoftwareAssetIds.Contains(installation.SoftwareAssetId))
                    .Select(installation => installation.DeviceAssetId)
                    .Distinct()
                    .Count(),
                AffectedVersionCount = affectedVersions.Count,
                AffectedVersions = affectedVersions,
                FirstSeenAt = relatedMatches.Min(match => match.FirstSeenAt),
                LastSeenAt = relatedMatches.Max(match => match.LastSeenAt),
                ResolvedAt = (DateTimeOffset?)null,
                Evidence = relatedMatches.Select(match => new
                {
                    match.Method,
                    Confidence = match.ConfidenceLabel,
                    match.Evidence,
                    match.FirstSeenAt,
                    match.LastSeenAt,
                    match.ResolvedAt,
                }),
            };
        })
            .Where(item => item is not null)
            .ToList();

        var versionSummary = activeInstallations
            .GroupBy(item => string.IsNullOrWhiteSpace(item.DetectedVersion) ? "Unknown" : item.DetectedVersion!)
            .Select(group => new
            {
                Version = group.Key,
                InstallCount = group.Count(),
                DeviceCount = group.Select(item => item.DeviceAssetId).Distinct().Count(),
            })
            .OrderByDescending(item => item.InstallCount)
            .ThenBy(item => item.Version, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        var deviceCriticalitySummary = activeInstallations
            .GroupBy(item => item.DeviceCriticality)
            .Select(group => new
            {
                Criticality = group.Key,
                DeviceCount = group.Select(item => item.DeviceAssetId).Distinct().Count(),
            })
            .OrderByDescending(item => item.DeviceCount)
            .ThenBy(item => item.Criticality, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var payload = new
        {
            software = new
            {
                tenantSoftware.Id,
                tenantSoftware.CanonicalName,
                tenantSoftware.CanonicalVendor,
                tenantSoftware.PrimaryCpe23Uri,
                tenantSoftware.NormalizationMethod,
                tenantSoftware.Confidence,
                tenantSoftware.FirstSeenAt,
                tenantSoftware.LastSeenAt,
                ActiveInstallCount = activeInstallations.Count,
                UniqueDeviceCount = uniqueDeviceCount,
                VulnerableInstallCount = activeInstallations.Count(item =>
                    matches.Any(match => match.SoftwareAssetId == item.SoftwareAssetId && match.ResolvedAt == null)
                ),
                ActiveVulnerabilityCount = vulnerabilityPayload.Count,
            },
            aliases,
            installationSummary = new
            {
                ActiveInstallCount = activeInstallations.Count,
                UniqueDeviceCount = uniqueDeviceCount,
                VersionSummary = versionSummary,
                DeviceCriticalitySummary = deviceCriticalitySummary,
            },
            vulnerabilities = vulnerabilityPayload,
        };

        var resolvedProfileResult = request.TenantAiProfileId.HasValue
            ? await tenantAiConfigurationResolver.ResolveByIdAsync(
                tenantSoftware.TenantId,
                request.TenantAiProfileId.Value,
                ct
            )
            : await tenantAiConfigurationResolver.ResolveDefaultAsync(tenantSoftware.TenantId, ct);

        if (!resolvedProfileResult.IsSuccess)
        {
            return BadRequest(new ProblemDetails { Title = resolvedProfileResult.Error ?? "Unable to resolve tenant AI configuration." });
        }

        var resolvedProfile = resolvedProfileResult.Value;
        var aiRequest = new AiTextGenerationRequest(
            "You are a PatchHound software vulnerability analyst. " +
            "Write a concise markdown report explaining the security risk of this software in the tenant. " +
            "Focus on what the software is, what the linked vulnerabilities do, how they are exploited or triggered, what makes the current tenant exposure risky, and the highest-priority remediation actions. " +
            "Do not list individual devices. Use counts, versions, and prevalence summaries only. " +
            "If external research is provided, use it to explain the vulnerabilities and exploitation mechanics, and cite sources when available. " +
            "Return markdown with these sections: Executive Summary, What This Software Is, How The Vulnerabilities Work, Tenant Exposure, Priority Actions.",
            JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })
        );

        var profile = resolvedProfile.Profile;
        if (profile.AllowExternalResearch)
        {
            if (
                profile.WebResearchMode == TenantAiWebResearchMode.ProviderNative
                && profile.ProviderType == TenantAiProviderType.OpenAi
            )
            {
                aiRequest = aiRequest with
                {
                    UseProviderNativeWebResearch = true,
                    AllowedDomains = ParseAllowedDomains(profile.AllowedDomains),
                    MaxResearchSources = profile.MaxResearchSources,
                    IncludeCitations = profile.IncludeCitations,
                };
            }
            else if (profile.WebResearchMode == TenantAiWebResearchMode.PatchHoundManaged)
            {
                var researchQuery = BuildSoftwareRiskResearchQuery(
                    tenantSoftware.CanonicalVendor,
                    tenantSoftware.CanonicalName,
                    tenantSoftware.PrimaryCpe23Uri,
                    vulnerabilities.Select(item => item.ExternalId).ToList()
                );
                var researchResult = await tenantAiResearchService.ResearchAsync(
                    resolvedProfile,
                    new AiWebResearchRequest(
                        researchQuery,
                        ParseAllowedDomains(profile.AllowedDomains),
                        profile.MaxResearchSources,
                        profile.IncludeCitations
                    ),
                    ct
                );

                if (researchResult.IsSuccess && !string.IsNullOrWhiteSpace(researchResult.Value.Context))
                {
                    aiRequest = aiRequest with { ExternalContext = researchResult.Value.Context };
                }
            }
        }

        var generationResult = await tenantAiTextGenerationService.GenerateResolvedAsync(
            resolvedProfile,
            aiRequest,
            ct
        );

        if (!generationResult.IsSuccess)
        {
            return BadRequest(new ProblemDetails { Title = generationResult.Error });
        }

        var generated = generationResult.Value;
        return Ok(
            new TenantSoftwareAiReportDto(
                id,
                generated.Content,
                generated.ProviderType,
                generated.ProfileName,
                generated.Model,
                generated.GeneratedAt
            )
        );
    }

    private static IReadOnlyList<string> ParseAllowedDomains(string? allowedDomains)
    {
        return (allowedDomains ?? string.Empty)
            .Split([',', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildSoftwareRiskResearchQuery(
        string? canonicalVendor,
        string canonicalName,
        string? primaryCpe23Uri,
        IReadOnlyList<string> cveIds
    )
    {
        var product = string.IsNullOrWhiteSpace(canonicalVendor)
            ? canonicalName
            : $"{canonicalVendor} {canonicalName}";
        var cveSummary = cveIds.Count == 0 ? string.Empty : $" Related CVEs: {string.Join(", ", cveIds.Take(5))}.";
        var cpeSummary = string.IsNullOrWhiteSpace(primaryCpe23Uri) ? string.Empty : $" CPE: {primaryCpe23Uri}.";
        return $"Explain the security risks and exploitation mechanics for {product}.{cpeSummary}{cveSummary}";
    }

    private static ExposureImpactCalculator.SoftwareImpactBreakdown BuildExposureImpactBreakdown(
        Guid tenantSoftwareId,
        int deviceCount,
        int highValueDeviceCount,
        IReadOnlyList<ExposureImpactVulnerabilityRow> vulnerabilities
    )
    {
        return ExposureImpactCalculator.CalculateSoftwareImpactBreakdown(
            new ExposureImpactCalculator.SoftwareImpactInput(
                tenantSoftwareId,
                deviceCount,
                highValueDeviceCount,
                vulnerabilities
                    .Select(item => new ExposureImpactCalculator.SoftwareVulnerabilityInput(
                        item.Severity,
                        item.CvssScore,
                        item.ExternalId
                    ))
                    .ToList()
            )
        );
    }

    private static ExposureImpactExplanationDto? ToExposureImpactExplanationDto(
        int deviceCount,
        int highValueDeviceCount,
        ExposureImpactCalculator.SoftwareImpactBreakdown breakdown
    )
    {
        if (deviceCount == 0 || breakdown.VulnerabilityFactors.Count == 0)
        {
            return null;
        }

        return new ExposureImpactExplanationDto(
            breakdown.ImpactScore,
            ExposureImpactCalculator.CalculationVersion,
            deviceCount,
            highValueDeviceCount,
            breakdown.DeviceReachWeight,
            breakdown.HighValueRatio,
            breakdown.HighValueBonus,
            breakdown.VulnerabilityFactors.Count,
            breakdown.RawVulnerabilitySum,
            breakdown.VulnerabilityComponent,
            breakdown.RawScore,
            breakdown.VulnerabilityFactors
                .Select(item => new ExposureImpactFactorDto(
                    item.ExternalId ?? "Unknown vulnerability",
                    item.Severity.ToString(),
                    item.CvssScore,
                    item.SeverityWeight,
                    item.NormalizedScore,
                    item.Contribution
                ))
                .ToList()
        );
    }

    private static IReadOnlyList<TenantSoftwareVulnerabilityEvidenceDto> ParseEvidence(
        string evidenceJson
    )
    {
        try
        {
            var rows = JsonSerializer.Deserialize<List<VulnerabilityEvidenceRow>>(
                evidenceJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            );
            return (rows ?? [])
                .Select(item => new TenantSoftwareVulnerabilityEvidenceDto(
                    item.Method,
                    item.Confidence,
                    item.Evidence,
                    item.FirstSeenAt,
                    item.LastSeenAt,
                    item.ResolvedAt
                ))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string NormalizeVersionKey(string? version)
    {
        return string.IsNullOrWhiteSpace(version) ? string.Empty : version.Trim();
    }

    private static string? RestoreVersion(string versionKey)
    {
        return string.IsNullOrWhiteSpace(versionKey) ? null : versionKey;
    }

    private static int GetMethodPriority(SoftwareVulnerabilityMatchMethod method)
    {
        return method switch
        {
            SoftwareVulnerabilityMatchMethod.DefenderDirect => 200,
            SoftwareVulnerabilityMatchMethod.CpeBinding => 100,
            _ => 0,
        };
    }

    private static int GetConfidencePriority(MatchConfidence confidence)
    {
        return confidence switch
        {
            MatchConfidence.High => 30,
            MatchConfidence.Medium => 20,
            _ => 10,
        };
    }

    private async Task<Guid?> ResolveActiveSoftwareSnapshotIdAsync(
        Guid tenantId,
        CancellationToken ct
    )
    {
        return await dbContext
            .TenantSourceConfigurations.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId
                && item.SourceKey == TenantSourceCatalog.DefenderSourceKey
            )
            .Select(item => item.ActiveSnapshotId)
            .FirstOrDefaultAsync(ct);
    }
}
