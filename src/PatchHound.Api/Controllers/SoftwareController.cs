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
            .SoftwareTenantRecords.AsNoTracking()
            .Where(item =>
                item.Id == id
                && item.TenantId == currentTenantId
                && item.SnapshotId == activeSnapshotId
            )
            .Select(item => new
            {
                item.Id,
                item.TenantId,
                item.SoftwareProductId,
                item.SoftwareProduct.CanonicalProductKey,
                item.FirstSeenAt,
                item.LastSeenAt,
                item.SoftwareProduct.Name,
                item.SoftwareProduct.Vendor,
                item.SoftwareProduct.Category,
                item.SoftwareProduct.Description,
                item.SoftwareProduct.DescriptionGeneratedAt,
                item.SoftwareProduct.DescriptionProviderType,
                item.SoftwareProduct.DescriptionProfileName,
                item.SoftwareProduct.DescriptionModel,
                item.SoftwareProduct.EolProductSlug,
                item.SoftwareProduct.EolDate,
                item.SoftwareProduct.EolLatestVersion,
                item.SoftwareProduct.EolIsLts,
                item.SoftwareProduct.EolSupportEndDate,
                item.SoftwareProduct.EolIsDiscontinued,
                item.SoftwareProduct.EolEnrichedAt,
                item.SoftwareProduct.SupplyChainRemediationPath,
                item.SoftwareProduct.SupplyChainInsightConfidence,
                item.SoftwareProduct.SupplyChainSourceFormat,
                item.SoftwareProduct.SupplyChainPrimaryComponentName,
                item.SoftwareProduct.SupplyChainPrimaryComponentVersion,
                item.SoftwareProduct.SupplyChainFixedVersion,
                item.SoftwareProduct.SupplyChainAffectedVulnerabilityCount,
                item.SoftwareProduct.SupplyChainSummary,
                item.SoftwareProduct.SupplyChainEnrichedAt,
            })
            .FirstOrDefaultAsync(ct);
        if (tenantSoftware is null)
        {
            return NotFound();
        }

        var installations = await dbContext
            .SoftwareProductInstallations.AsNoTracking()
            .Where(item => item.TenantSoftwareId == id && item.SnapshotId == activeSnapshotId)
            .ToListAsync(ct);

        var activeInstallations = installations.Where(item => item.IsActive).ToList();
        var softwareAssetIds = installations.Select(item => item.SoftwareAssetId).Distinct().ToList();

        var openExposures = await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(e => e.TenantId == currentTenantId
                && e.SoftwareProductId == tenantSoftware.SoftwareProductId
                && e.Status == ExposureStatus.Open)
            .Select(e => new { e.DeviceId, e.VulnerabilityId })
            .ToListAsync(ct);
        var openDeviceIds = openExposures.Select(e => e.DeviceId).ToHashSet();
        var openMatchDeviceSoftwareAssetIds = activeInstallations
            .Where(item => openDeviceIds.Contains(item.DeviceAssetId))
            .Select(item => item.SoftwareAssetId)
            .ToHashSet();

        var versionCohorts = activeInstallations
            .GroupBy(item => NormalizeVersionKey(item.DetectedVersion))
            .Select(group =>
            {
                var groupDeviceIds = group.Select(item => item.DeviceAssetId).ToHashSet();
                var cohortVulnCount = openExposures
                    .Where(e => groupDeviceIds.Contains(e.DeviceId))
                    .Select(e => e.VulnerabilityId)
                    .Distinct()
                    .Count();
                return new TenantSoftwareVersionCohortDto(
                    RestoreVersion(group.Key),
                    group.Count(),
                    groupDeviceIds.Count,
                    cohortVulnCount,
                    group.Min(item => item.FirstSeenAt),
                    group.Max(item => item.LastSeenAt)
                );
            })
            .OrderByDescending(item => item.ActiveInstallCount)
            .ThenByDescending(item => item.ActiveVulnerabilityCount)
            .ThenBy(item => item.Version ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var openVulnerabilityIds = openExposures.Select(e => e.VulnerabilityId).Distinct().ToList();
        var openVulnerabilityRows = await dbContext.Vulnerabilities.AsNoTracking()
            .Where(v => openVulnerabilityIds.Contains(v.Id))
            .Select(v => new ExposureImpactVulnerabilityRow(v.ExternalId, v.VendorSeverity, v.CvssScore))
            .ToListAsync(ct);

        var activeDeviceIds = activeInstallations
            .Select(item => item.DeviceAssetId)
            .Distinct()
            .ToList();
        var highValueDeviceCount = activeDeviceIds.Count == 0
            ? 0
            : await dbContext.Devices.AsNoTracking()
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
                tenantSoftware.SoftwareProductId,
                softwareAssetIds.FirstOrDefault() is var primaryAssetId && primaryAssetId != Guid.Empty ? primaryAssetId : null,
                tenantSoftware.Name,
                tenantSoftware.Vendor,
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
                    openMatchDeviceSoftwareAssetIds.Contains(item.SoftwareAssetId)
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

        var tenantSoftware = await dbContext.SoftwareTenantRecords.FirstOrDefaultAsync(
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
            .SoftwareTenantRecords.AsNoTracking()
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
                result.Value.SoftwareProductId,
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
                job.SoftwareProductId,
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
            .SoftwareTenantRecords.AsNoTracking()
            .Where(item => item.TenantId == currentTenantId);

        var activeSnapshotId = await ResolveActiveSoftwareSnapshotIdAsync(currentTenantId, ct);
        query = query.Where(item => item.SnapshotId == activeSnapshotId);
        query = query.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query = query.Where(item =>
                item.SoftwareProduct.Name.Contains(filter.Search)
                || (
                    item.SoftwareProduct.Vendor != null
                    && item.SoftwareProduct.Vendor.Contains(filter.Search)
                )
            );
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            query = query.Where(item =>
                item.SoftwareProduct.Category == filter.Category
            );
        }

        if (filter.VulnerableOnly == true)
        {
            query = query.Where(item => dbContext.DeviceVulnerabilityExposures.Any(e =>
                e.TenantId == currentTenantId
                && e.SoftwareProductId == item.SoftwareProductId
                && e.Status == ExposureStatus.Open));
        }

        if (filter.MissedMaintenanceWindow == true)
        {
            // Maintenance-window semantics are not yet represented on RemediationCase; filter intentionally no-ops.
            query = query.Where(_ => false);
        }

        var totalCount = await query.CountAsync(ct);
        var rows = await query
            .Select(item => new
            {
                item.Id,
                item.SoftwareProductId,
                CanonicalProductKey = item.SoftwareProduct.CanonicalProductKey,
                CanonicalName = item.SoftwareProduct.Name,
                CanonicalVendor = item.SoftwareProduct.Vendor,
                Category = item.SoftwareProduct.Category,
                CurrentRiskScore = (decimal?)null,
                ActiveInstallCount = dbContext
                    .SoftwareProductInstallations
                    .Where(installation =>
                        installation.TenantSoftwareId == item.Id
                        && installation.SnapshotId == activeSnapshotId
                        && installation.IsActive
                    )
                    .Count(),
                UniqueDeviceCount = dbContext
                    .SoftwareProductInstallations
                    .Where(installation =>
                        installation.TenantSoftwareId == item.Id
                        && installation.SnapshotId == activeSnapshotId
                        && installation.IsActive
                    )
                    .Select(installation => installation.DeviceAssetId)
                    .Distinct()
                    .Count(),
                ActiveVulnerabilityCount = dbContext.DeviceVulnerabilityExposures
                    .Where(e =>
                        e.TenantId == currentTenantId
                        && e.SoftwareProductId == item.SoftwareProductId
                        && e.Status == ExposureStatus.Open)
                    .Select(e => e.VulnerabilityId)
                    .Distinct()
                    .Count(),
                VersionCount = dbContext
                    .SoftwareProductInstallations
                    .Where(installation =>
                        installation.TenantSoftwareId == item.Id
                        && installation.SnapshotId == activeSnapshotId
                        && installation.IsActive
                    )
                    .Select(installation => installation.DetectedVersion ?? string.Empty)
                    .Distinct()
                    .Count(version => version != string.Empty),
                LastSeenAt = dbContext
                    .SoftwareProductInstallations
                    .Where(installation =>
                        installation.TenantSoftwareId == item.Id
                        && installation.SnapshotId == activeSnapshotId
                    )
                    .Select(installation => (DateTimeOffset?)installation.LastSeenAt)
                    .Max(),
                // Maintenance-window semantics not yet modeled on RemediationCase; surfaces null until feature returns.
                MaintenanceWindowDate = (DateTimeOffset?)null,
                ExposureImpactScore = dbContext
                    .SoftwareProductInstallations
                    .Where(installation =>
                        installation.TenantSoftwareId == item.Id
                        && installation.SnapshotId == activeSnapshotId
                        && installation.IsActive
                    )
                    .Select(installation => installation.DeviceAsset.ExposureImpactScore)
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
                        item.SoftwareProductId,
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
            .SoftwareTenantRecords.AsNoTracking()
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
            .SoftwareProductInstallations.AsNoTracking()
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
                DeviceName = item.DeviceAsset.ComputerDnsName ?? item.DeviceAsset.Name,
                DeviceCriticality = item.DeviceAsset.Criticality.ToString(),
                item.SoftwareAssetId,
                SoftwareAssetName = item.TenantSoftware.SoftwareProduct.Name,
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
                    .SecurityProfiles.Where(profile =>
                        profile.Id == item.DeviceAsset.SecurityProfileId
                    )
                    .Select(profile => profile.Name)
                    .FirstOrDefault(),
                OpenVulnerabilityCount = dbContext.DeviceVulnerabilityExposures
                    .Where(e =>
                        e.TenantId == currentTenantId
                        && e.DeviceId == item.DeviceAssetId
                        && e.SoftwareProductId == item.TenantSoftware.SoftwareProductId
                        && e.Status == ExposureStatus.Open)
                    .Select(e => e.VulnerabilityId)
                    .Distinct()
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
            .SoftwareTenantRecords.AsNoTracking()
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

        var productId = await dbContext.SoftwareTenantRecords.AsNoTracking()
            .Where(item => item.Id == tenantSoftware.Id)
            .Select(item => item.SoftwareProductId)
            .FirstAsync(ct);

        var exposures = await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(e => e.TenantId == currentTenantId && e.SoftwareProductId == productId)
            .Select(e => new
            {
                e.VulnerabilityId,
                e.DeviceId,
                e.InstalledSoftwareId,
                e.MatchedVersion,
                e.MatchSource,
                e.Status,
                e.FirstObservedAt,
                e.LastObservedAt,
                e.ResolvedAt,
            })
            .ToListAsync(ct);

        if (exposures.Count == 0)
        {
            return Ok(Array.Empty<TenantSoftwareVulnerabilityDto>());
        }

        var vulnIds = exposures.Select(e => e.VulnerabilityId).Distinct().ToList();
        var vulnRows = await dbContext.Vulnerabilities.AsNoTracking()
            .Where(v => vulnIds.Contains(v.Id))
            .Select(v => new
            {
                v.Id,
                v.ExternalId,
                v.Title,
                v.Description,
                v.VendorSeverity,
                v.CvssScore,
                v.PublishedDate,
                v.Source,
            })
            .ToListAsync(ct);

        var results = vulnRows
            .Select(v =>
            {
                var rows = exposures.Where(e => e.VulnerabilityId == v.Id).ToList();
                var versions = rows
                    .Select(r => r.MatchedVersion)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var bestMatchSource = rows
                    .OrderByDescending(r => r.MatchSource == ExposureMatchSource.Cpe)
                    .Select(r => r.MatchSource)
                    .First();
                var evidence = rows
                    .GroupBy(r => r.MatchSource)
                    .Select(g => new TenantSoftwareVulnerabilityEvidenceDto(
                        g.Key.ToString(),
                        "Observed",
                        $"{g.Count()} exposure(s)",
                        g.Min(r => r.FirstObservedAt),
                        g.Max(r => r.LastObservedAt),
                        g.Any(r => r.ResolvedAt is null) ? null : g.Max(r => r.ResolvedAt)))
                    .ToList();
                return new TenantSoftwareVulnerabilityDto(
                    v.Id,
                    v.Id,
                    v.ExternalId,
                    v.Title,
                    v.Description,
                    v.VendorSeverity.ToString(),
                    v.CvssScore,
                    v.PublishedDate,
                    v.Source,
                    bestMatchSource.ToString(),
                    "Observed",
                    rows.Count,
                    rows.Select(r => r.DeviceId).Distinct().Count(),
                    versions.Count,
                    versions,
                    rows.Min(r => r.FirstObservedAt),
                    rows.Max(r => r.LastObservedAt),
                    rows.Any(r => r.ResolvedAt is null) ? null : rows.Max(r => r.ResolvedAt),
                    evidence);
            })
            .OrderByDescending(item => item.CvssScore ?? 0m)
            .ThenByDescending(item => item.AffectedDeviceCount)
            .ThenBy(item => item.ExternalId)
            .ToList();

        return Ok(results);
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
            .SoftwareTenantRecords.AsNoTracking()
            .Where(item =>
                item.Id == id
                && item.TenantId == currentTenantId
                && item.SnapshotId == activeSnapshotId
            )
            .Select(item => new
            {
                item.Id,
                item.TenantId,
                item.SoftwareProductId,
                item.FirstSeenAt,
                item.LastSeenAt,
                Name = item.SoftwareProduct.Name,
                Vendor = item.SoftwareProduct.Vendor,
                PrimaryCpe23Uri = item.SoftwareProduct.PrimaryCpe23Uri,
                NormalizationMethod = item.SoftwareProduct.NormalizationMethod.ToString(),
                Confidence = item.SoftwareProduct.Confidence.ToString(),
            })
            .FirstOrDefaultAsync(ct);
        if (tenantSoftware is null)
        {
            return NotFound(new ProblemDetails { Title = "Tenant software not found" });
        }

        var aliases = await dbContext
            .SoftwareProductAliases.AsNoTracking()
            .Where(item => item.SoftwareProductId == tenantSoftware.SoftwareProductId)
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
            .SoftwareProductInstallations.AsNoTracking()
            .Where(item => item.TenantSoftwareId == id && item.SnapshotId == activeSnapshotId)
            .Select(item => new
            {
                item.DeviceAssetId,
                DeviceName = item.DeviceAsset.ComputerDnsName ?? item.DeviceAsset.Name,
                DeviceCriticality = item.DeviceAsset.Criticality.ToString(),
                item.SoftwareAssetId,
                SoftwareAssetName = item.TenantSoftware.SoftwareProduct.Name,
                item.DetectedVersion,
                item.FirstSeenAt,
                item.LastSeenAt,
                item.RemovedAt,
                item.IsActive,
                item.CurrentEpisodeNumber,
            })
            .ToListAsync(ct);

        var activeInstallations = installations.Where(item => item.IsActive).ToList();
        var uniqueDeviceCount = activeInstallations.Select(item => item.DeviceAssetId).Distinct().Count();

        var productExposures = await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(e => e.TenantId == currentTenantId
                && e.SoftwareProductId == tenantSoftware.SoftwareProductId
                && e.Status == ExposureStatus.Open)
            .Select(e => new { e.DeviceId, e.VulnerabilityId, e.MatchedVersion })
            .ToListAsync(ct);

        var aiVulnIds = productExposures.Select(e => e.VulnerabilityId).Distinct().ToList();
        var aiVulnRows = aiVulnIds.Count == 0
            ? []
            : await dbContext.Vulnerabilities.AsNoTracking()
                .Where(v => aiVulnIds.Contains(v.Id))
                .Select(v => new
                {
                    v.Id,
                    v.ExternalId,
                    v.Title,
                    v.VendorSeverity,
                    v.CvssScore,
                    v.PublishedDate,
                })
                .ToListAsync(ct);

        var vulnerabilityExternalIds = aiVulnRows.Select(v => v.ExternalId).ToArray();
        var vulnerabilityPayload = aiVulnRows
            .Select(v =>
            {
                var rows = productExposures.Where(e => e.VulnerabilityId == v.Id).ToList();
                return (object?)new
                {
                    v.ExternalId,
                    v.Title,
                    Severity = v.VendorSeverity.ToString(),
                    v.CvssScore,
                    v.PublishedDate,
                    AffectedDeviceCount = rows.Select(r => r.DeviceId).Distinct().Count(),
                    AffectedInstallCount = rows.Count,
                };
            })
            .ToArray();
        var vulnerableInstallCount = activeInstallations.Count(item =>
            productExposures.Any(e => e.DeviceId == item.DeviceAssetId));

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
                tenantSoftware.Name,
                tenantSoftware.Vendor,
                tenantSoftware.PrimaryCpe23Uri,
                tenantSoftware.NormalizationMethod,
                tenantSoftware.Confidence,
                tenantSoftware.FirstSeenAt,
                tenantSoftware.LastSeenAt,
                ActiveInstallCount = activeInstallations.Count,
                UniqueDeviceCount = uniqueDeviceCount,
                VulnerableInstallCount = vulnerableInstallCount,
                ActiveVulnerabilityCount = vulnerabilityPayload.Length,
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
                    tenantSoftware.Vendor,
                    tenantSoftware.Name,
                    tenantSoftware.PrimaryCpe23Uri,
                    vulnerabilityExternalIds
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
