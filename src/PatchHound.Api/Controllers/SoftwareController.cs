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

        // id is the SoftwareProductId in the new pipeline
        var softwareProduct = await dbContext.SoftwareProducts.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Vendor,
                p.Category,
                p.Description,
                p.DescriptionGeneratedAt,
                p.DescriptionProviderType,
                p.DescriptionProfileName,
                p.DescriptionModel,
                p.EolProductSlug,
                p.EolDate,
                p.EolLatestVersion,
                p.EolIsLts,
                p.EolSupportEndDate,
                p.EolIsDiscontinued,
                p.EolEnrichedAt,
                p.SupplyChainRemediationPath,
                p.SupplyChainInsightConfidence,
                p.SupplyChainSourceFormat,
                p.SupplyChainPrimaryComponentName,
                p.SupplyChainPrimaryComponentVersion,
                p.SupplyChainFixedVersion,
                p.SupplyChainAffectedVulnerabilityCount,
                p.SupplyChainSummary,
                p.SupplyChainEnrichedAt,
            })
            .FirstOrDefaultAsync(ct);
        if (softwareProduct is null)
        {
            return NotFound();
        }

        // Verify this software is actually installed in the current tenant
        var hasTenantInstall = await dbContext.InstalledSoftware.AsNoTracking()
            .AnyAsync(i => i.TenantId == currentTenantId && i.SoftwareProductId == id, ct);
        if (!hasTenantInstall)
        {
            return NotFound();
        }

        var installations = await dbContext.InstalledSoftware.AsNoTracking()
            .Where(i => i.TenantId == currentTenantId && i.SoftwareProductId == id)
            .Select(i => new
            {
                i.DeviceId,
                i.Version,
                i.FirstSeenAt,
                i.LastSeenAt,
            })
            .ToListAsync(ct);

        var openExposures = await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(e => e.TenantId == currentTenantId
                && e.SoftwareProductId == id
                && e.Status == ExposureStatus.Open)
            .Select(e => new { e.DeviceId, e.VulnerabilityId })
            .ToListAsync(ct);
        var openDeviceIds = openExposures.Select(e => e.DeviceId).ToHashSet();
        var vulnerableInstallCount = installations.Count(i => openDeviceIds.Contains(i.DeviceId));

        var versionCohorts = installations
            .GroupBy(i => NormalizeVersionKey(i.Version))
            .Select(group =>
            {
                var groupDeviceIds = group.Select(i => i.DeviceId).ToHashSet();
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
                    group.Min(i => i.FirstSeenAt),
                    group.Max(i => i.LastSeenAt)
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

        var activeDeviceIds = installations.Select(i => i.DeviceId).Distinct().ToList();
        var highValueDeviceCount = activeDeviceIds.Count == 0
            ? 0
            : await dbContext.Devices.AsNoTracking()
                .Where(d => activeDeviceIds.Contains(d.Id))
                .CountAsync(d =>
                    d.Criticality == Criticality.High || d.Criticality == Criticality.Critical,
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

        var firstSeenAt = installations.Count == 0 ? (DateTimeOffset?)null : installations.Min(i => i.FirstSeenAt);
        var lastSeenAt = installations.Count == 0 ? (DateTimeOffset?)null : installations.Max(i => i.LastSeenAt);

        return Ok(
            new TenantSoftwareDetailDto(
                softwareProduct.Id,
                softwareProduct.Id,
                null, // PrimarySoftwareAssetId — not applicable in new pipeline
                softwareProduct.Name,
                softwareProduct.Vendor,
                softwareProduct.Category,
                softwareProduct.Description,
                softwareProduct.DescriptionGeneratedAt,
                softwareProduct.DescriptionProviderType,
                softwareProduct.DescriptionProfileName,
                softwareProduct.DescriptionModel,
                firstSeenAt,
                lastSeenAt,
                installations.Count,
                activeDeviceIds.Count,
                vulnerableInstallCount,
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
                softwareProduct.EolEnrichedAt.HasValue
                    ? new SoftwareLifecycleDto(
                        softwareProduct.EolDate,
                        softwareProduct.EolLatestVersion,
                        softwareProduct.EolIsLts,
                        softwareProduct.EolSupportEndDate,
                        softwareProduct.EolIsDiscontinued,
                        softwareProduct.EolEnrichedAt,
                        softwareProduct.EolProductSlug
                    )
                    : null,
                softwareProduct.SupplyChainEnrichedAt.HasValue
                    ? new SupplyChainInsightDto(
                        softwareProduct.SupplyChainRemediationPath.ToString(),
                        softwareProduct.SupplyChainInsightConfidence.ToString(),
                        softwareProduct.SupplyChainSourceFormat,
                        softwareProduct.SupplyChainPrimaryComponentName,
                        softwareProduct.SupplyChainPrimaryComponentVersion,
                        softwareProduct.SupplyChainFixedVersion,
                        softwareProduct.SupplyChainAffectedVulnerabilityCount,
                        softwareProduct.SupplyChainSummary ?? string.Empty,
                        softwareProduct.SupplyChainEnrichedAt
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

        // id is the SoftwareProductId in the new pipeline
        var tenantSoftwareExists = await dbContext.InstalledSoftware.AsNoTracking()
            .AnyAsync(i => i.TenantId == currentTenantId && i.SoftwareProductId == id, ct);
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

        // Base query: distinct software products seen in this tenant
        var installedQuery = dbContext.InstalledSoftware.AsNoTracking()
            .Where(i => i.TenantId == currentTenantId);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            installedQuery = installedQuery.Where(i =>
                i.SoftwareProductId == dbContext.SoftwareProducts
                    .Where(p =>
                        p.Name.Contains(filter.Search)
                        || (p.Vendor != null && p.Vendor.Contains(filter.Search)))
                    .Select(p => p.Id)
                    .FirstOrDefault()
                || dbContext.SoftwareProducts
                    .Any(p =>
                        p.Id == i.SoftwareProductId
                        && (p.Name.Contains(filter.Search)
                            || (p.Vendor != null && p.Vendor.Contains(filter.Search))))
            );
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            installedQuery = installedQuery.Where(i =>
                dbContext.SoftwareProducts.Any(p =>
                    p.Id == i.SoftwareProductId && p.Category == filter.Category));
        }

        if (filter.VulnerableOnly == true)
        {
            installedQuery = installedQuery.Where(i =>
                dbContext.DeviceVulnerabilityExposures.Any(e =>
                    e.TenantId == currentTenantId
                    && e.SoftwareProductId == i.SoftwareProductId
                    && e.Status == ExposureStatus.Open));
        }

        // MissedMaintenanceWindow semantics not yet modeled; filter intentionally no-ops.
        if (filter.MissedMaintenanceWindow == true)
        {
            installedQuery = installedQuery.Where(_ => false);
        }

        // Distinct software product IDs matching the filters
        var distinctProductIds = installedQuery
            .Select(i => i.SoftwareProductId)
            .Distinct();

        var totalCount = await distinctProductIds.CountAsync(ct);

        var rows = await distinctProductIds
            .Join(
                dbContext.SoftwareProducts.AsNoTracking(),
                productId => productId,
                product => product.Id,
                (productId, product) => new
                {
                    Id = productId,
                    SoftwareProductId = productId,
                    product.Name,
                    product.Vendor,
                    product.Category,
                    product.EolEnrichedAt,
                    product.SupplyChainEnrichedAt,
                }
            )
            .Select(item => new
            {
                item.Id,
                item.SoftwareProductId,
                CanonicalName = item.Name,
                CanonicalVendor = item.Vendor,
                item.Category,
                CurrentRiskScore = (decimal?)null,
                ActiveInstallCount = dbContext.InstalledSoftware
                    .Count(i => i.TenantId == currentTenantId && i.SoftwareProductId == item.Id),
                UniqueDeviceCount = dbContext.InstalledSoftware
                    .Where(i => i.TenantId == currentTenantId && i.SoftwareProductId == item.Id)
                    .Select(i => i.DeviceId)
                    .Distinct()
                    .Count(),
                ActiveVulnerabilityCount = dbContext.DeviceVulnerabilityExposures
                    .Where(e =>
                        e.TenantId == currentTenantId
                        && e.SoftwareProductId == item.Id
                        && e.Status == ExposureStatus.Open)
                    .Select(e => e.VulnerabilityId)
                    .Distinct()
                    .Count(),
                VersionCount = dbContext.InstalledSoftware
                    .Where(i => i.TenantId == currentTenantId && i.SoftwareProductId == item.Id && i.Version != "")
                    .Select(i => i.Version)
                    .Distinct()
                    .Count(),
                LastSeenAt = (DateTimeOffset?)dbContext.InstalledSoftware
                    .Where(i => i.TenantId == currentTenantId && i.SoftwareProductId == item.Id)
                    .Max(i => (DateTimeOffset?)i.LastSeenAt),
                MaintenanceWindowDate = (DateTimeOffset?)null,
                ExposureImpactScore = dbContext.InstalledSoftware
                    .Where(i => i.TenantId == currentTenantId && i.SoftwareProductId == item.Id)
                    .Join(
                        dbContext.Devices.AsNoTracking(),
                        ins => ins.DeviceId,
                        dev => dev.Id,
                        (ins, dev) => dev.ExposureImpactScore)
                    .Max(),
            })
            .OrderByDescending(item => item.ActiveVulnerabilityCount)
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

        // id is the SoftwareProductId in the new pipeline
        var hasTenantInstall = await dbContext.InstalledSoftware.AsNoTracking()
            .AnyAsync(i => i.TenantId == currentTenantId && i.SoftwareProductId == id, ct);
        if (!hasTenantInstall)
        {
            return NotFound();
        }

        var softwareName = await dbContext.SoftwareProducts.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var installationsQuery = dbContext.InstalledSoftware.AsNoTracking()
            .Where(i => i.TenantId == currentTenantId && i.SoftwareProductId == id);

        if (query.Version is not null)
        {
            if (string.IsNullOrWhiteSpace(query.Version))
            {
                installationsQuery = installationsQuery.Where(i => i.Version == "");
            }
            else
            {
                var version = query.Version.Trim();
                installationsQuery = installationsQuery.Where(i => i.Version == version);
            }
        }

        // ActiveOnly has no meaning in InstalledSoftware (rows are always current observations)
        // kept for API compatibility

        var totalCount = await installationsQuery.CountAsync(ct);
        var rows = await installationsQuery
            .OrderByDescending(i => i.LastSeenAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Join(
                dbContext.Devices.AsNoTracking(),
                i => i.DeviceId,
                d => d.Id,
                (i, d) => new
                {
                    i.Id,
                    DeviceId = i.DeviceId,
                    DeviceName = d.ComputerDnsName ?? d.Name,
                    DeviceCriticality = d.Criticality.ToString(),
                    i.Version,
                    i.FirstSeenAt,
                    i.LastSeenAt,
                    d.OwnerUserId,
                    d.OwnerTeamId,
                    d.SecurityProfileId,
                })
            .Select(item => new
            {
                item.Id,
                item.DeviceId,
                item.DeviceName,
                item.DeviceCriticality,
                item.Version,
                item.FirstSeenAt,
                item.LastSeenAt,
                item.OwnerUserId,
                item.OwnerTeamId,
                OwnerUserName = dbContext.Users
                    .Where(u => u.Id == item.OwnerUserId)
                    .Select(u => u.DisplayName)
                    .FirstOrDefault(),
                OwnerTeamName = dbContext.Teams
                    .Where(t => t.Id == item.OwnerTeamId)
                    .Select(t => t.Name)
                    .FirstOrDefault(),
                SecurityProfileName = dbContext.SecurityProfiles
                    .Where(p => p.Id == item.SecurityProfileId)
                    .Select(p => p.Name)
                    .FirstOrDefault(),
                OpenVulnerabilityCount = dbContext.DeviceVulnerabilityExposures
                    .Where(e =>
                        e.TenantId == currentTenantId
                        && e.DeviceId == item.DeviceId
                        && e.SoftwareProductId == id
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
                        item.DeviceId,
                        item.DeviceName,
                        item.DeviceCriticality,
                        item.Id,          // InstalledSoftware.Id as the "asset id"
                        softwareName,
                        string.IsNullOrEmpty(item.Version) ? null : item.Version,
                        item.FirstSeenAt,
                        item.LastSeenAt,
                        null,             // RemovedAt — not tracked in new pipeline
                        true,             // IsActive — presence in table means active
                        0,                // CurrentEpisodeNumber — not tracked
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

        // id is the SoftwareProductId in the new pipeline
        var hasTenantInstall = await dbContext.InstalledSoftware.AsNoTracking()
            .AnyAsync(i => i.TenantId == currentTenantId && i.SoftwareProductId == id, ct);
        if (!hasTenantInstall)
        {
            return NotFound();
        }

        var exposures = await dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(e => e.TenantId == currentTenantId && e.SoftwareProductId == id)
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
        var installations = await dbContext.InstalledSoftware.AsNoTracking()
            .Where(i => i.TenantId == currentTenantId && i.SoftwareProductId == tenantSoftware.SoftwareProductId)
            .Join(
                dbContext.Devices.AsNoTracking(),
                i => i.DeviceId,
                d => d.Id,
                (i, d) => new
                {
                    DeviceAssetId = i.DeviceId,
                    DeviceName = d.ComputerDnsName ?? d.Name,
                    DeviceCriticality = d.Criticality.ToString(),
                    i.Version,
                    i.FirstSeenAt,
                    i.LastSeenAt,
                    IsActive = true,
                })
            .ToListAsync(ct);

        var activeInstallations = installations;
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
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Version) ? "Unknown" : item.Version!)
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
