using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Software;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/software")]
[Authorize]
public class SoftwareController(
    PatchHoundDbContext dbContext,
    TenantAiTextGenerationService tenantAiTextGenerationService,
    SoftwareDescriptionJobService softwareDescriptionJobService,
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

        var tenantSoftware = await dbContext
            .TenantSoftware.AsNoTracking()
            .Where(item => item.Id == id && item.TenantId == currentTenantId)
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
                item.NormalizedSoftware.Description,
                item.NormalizedSoftware.DescriptionGeneratedAt,
                item.NormalizedSoftware.DescriptionProviderType,
                item.NormalizedSoftware.DescriptionProfileName,
                item.NormalizedSoftware.DescriptionModel,
                NormalizationMethod = item.NormalizedSoftware.NormalizationMethod.ToString(),
                Confidence = item.NormalizedSoftware.Confidence.ToString(),
            })
            .FirstOrDefaultAsync(ct);
        if (tenantSoftware is null)
        {
            return NotFound();
        }

        var aliases = await dbContext
            .NormalizedSoftwareAliases.AsNoTracking()
            .Where(item => item.NormalizedSoftwareId == tenantSoftware.NormalizedSoftwareId)
            .OrderBy(item => item.SourceSystem)
            .ThenBy(item => item.ExternalSoftwareId)
            .ToListAsync(ct);

        var installations = await dbContext
            .NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item => item.TenantSoftwareId == id)
            .ToListAsync(ct);

        var activeInstallations = installations.Where(item => item.IsActive).ToList();
        var softwareAssetIds = installations.Select(item => item.SoftwareAssetId).Distinct().ToList();

        var openMatches = await dbContext
            .SoftwareVulnerabilityMatches.AsNoTracking()
            .Where(match => match.ResolvedAt == null && softwareAssetIds.Contains(match.SoftwareAssetId))
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

        return Ok(
            new TenantSoftwareDetailDto(
                tenantSoftware.Id,
                tenantSoftware.NormalizedSoftwareId,
                tenantSoftware.CanonicalName,
                tenantSoftware.CanonicalVendor,
                tenantSoftware.PrimaryCpe23Uri,
                tenantSoftware.Description,
                tenantSoftware.DescriptionGeneratedAt,
                tenantSoftware.DescriptionProviderType,
                tenantSoftware.DescriptionProfileName,
                tenantSoftware.DescriptionModel,
                tenantSoftware.NormalizationMethod,
                tenantSoftware.Confidence,
                installations.Count == 0 ? tenantSoftware.FirstSeenAt : installations.Min(item => item.FirstSeenAt),
                installations.Count == 0 ? tenantSoftware.LastSeenAt : installations.Max(item => item.LastSeenAt),
                activeInstallations.Count,
                activeInstallations.Select(item => item.DeviceAssetId).Distinct().Count(),
                activeInstallations.Count(item =>
                    openMatchSoftwareAssetIds.Contains(item.SoftwareAssetId)
                ),
                await dbContext
                    .NormalizedSoftwareVulnerabilityProjections.AsNoTracking()
                    .CountAsync(item => item.TenantSoftwareId == id && item.ResolvedAt == null, ct),
                versionCohorts.Count,
                versionCohorts,
                aliases
                    .Select(item => new TenantSoftwareSourceAliasDto(
                        item.SourceSystem.ToString(),
                        item.ExternalSoftwareId,
                        item.RawName,
                        item.RawVendor,
                        item.RawVersion,
                        item.AliasConfidence.ToString(),
                        item.MatchReason
                    ))
                    .ToList()
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
            .Where(item => item.TenantId == currentTenantId)
            .AsQueryable();

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

        if (!string.IsNullOrWhiteSpace(filter.Confidence))
        {
            query = query.Where(item =>
                item.NormalizedSoftware.Confidence.ToString() == filter.Confidence
            );
        }

        if (filter.BoundOnly == true)
        {
            query = query.Where(item =>
                item.NormalizedSoftware.PrimaryCpe23Uri != null
                && item.NormalizedSoftware.PrimaryCpe23Uri != ""
            );
        }

        if (filter.VulnerableOnly == true)
        {
            query = query.Where(item =>
                dbContext.NormalizedSoftwareVulnerabilityProjections.Any(projection =>
                    projection.TenantSoftwareId == item.Id && projection.ResolvedAt == null
                )
            );
        }

        var totalCount = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(item => item.NormalizedSoftware.CanonicalName)
            .ThenBy(item => item.Id)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(item => new
            {
                item.Id,
                item.NormalizedSoftwareId,
                CanonicalName = item.NormalizedSoftware.CanonicalName,
                CanonicalVendor = item.NormalizedSoftware.CanonicalVendor,
                Confidence = item.NormalizedSoftware.Confidence.ToString(),
                NormalizationMethod = item.NormalizedSoftware.NormalizationMethod.ToString(),
                PrimaryCpe23Uri = item.NormalizedSoftware.PrimaryCpe23Uri,
                ActiveInstallCount = dbContext
                    .NormalizedSoftwareInstallations
                    .Where(installation => installation.TenantSoftwareId == item.Id && installation.IsActive)
                    .Count(),
                UniqueDeviceCount = dbContext
                    .NormalizedSoftwareInstallations
                    .Where(installation => installation.TenantSoftwareId == item.Id && installation.IsActive)
                    .Select(installation => installation.DeviceAssetId)
                    .Distinct()
                    .Count(),
                ActiveVulnerabilityCount = dbContext
                    .NormalizedSoftwareVulnerabilityProjections
                    .Where(projection => projection.TenantSoftwareId == item.Id && projection.ResolvedAt == null)
                    .Count(),
                VersionCount = dbContext
                    .NormalizedSoftwareInstallations
                    .Where(installation => installation.TenantSoftwareId == item.Id && installation.IsActive)
                    .Select(installation => installation.DetectedVersion ?? string.Empty)
                    .Distinct()
                    .Count(version => version != string.Empty),
                LastSeenAt = dbContext
                    .NormalizedSoftwareInstallations
                    .Where(installation => installation.TenantSoftwareId == item.Id)
                    .Select(installation => (DateTimeOffset?)installation.LastSeenAt)
                    .Max(),
            })
            .ToListAsync(ct);

        return Ok(
            new PagedResponse<TenantSoftwareListItemDto>(
                rows
                    .Select(item => new TenantSoftwareListItemDto(
                        item.Id,
                        item.NormalizedSoftwareId,
                        item.CanonicalName,
                        item.CanonicalVendor,
                        item.Confidence,
                        item.NormalizationMethod,
                        item.PrimaryCpe23Uri,
                        item.ActiveInstallCount,
                        item.UniqueDeviceCount,
                        item.ActiveVulnerabilityCount,
                        item.VersionCount,
                        item.LastSeenAt
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

        var tenantSoftware = await dbContext
            .TenantSoftware.AsNoTracking()
            .Where(item => item.Id == id && item.TenantId == currentTenantId)
            .Select(item => new { item.Id, item.TenantId })
            .FirstOrDefaultAsync(ct);
        if (tenantSoftware is null)
        {
            return NotFound();
        }

        var installationsQuery = dbContext
            .NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item => item.TenantSoftwareId == id);

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
                        item.OwnerTeamId,
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

        var tenantSoftware = await dbContext
            .TenantSoftware.AsNoTracking()
            .Where(item => item.Id == id && item.TenantId == currentTenantId)
            .Select(item => new { item.Id, item.TenantId })
            .FirstOrDefaultAsync(ct);
        if (tenantSoftware is null)
        {
            return NotFound();
        }

        var projections = await dbContext
            .NormalizedSoftwareVulnerabilityProjections.AsNoTracking()
            .Where(item => item.TenantSoftwareId == id)
            .Join(
                dbContext.VulnerabilityDefinitions.AsNoTracking(),
                projection => projection.VulnerabilityDefinitionId,
                vulnerabilityDefinition => vulnerabilityDefinition.Id,
                (projection, vulnerabilityDefinition) => new { projection, vulnerabilityDefinition }
            )
            .OrderByDescending(item => item.vulnerabilityDefinition.CvssScore)
            .ThenByDescending(item => item.vulnerabilityDefinition.PublishedDate)
            .ToListAsync(ct);

        var tenantVulnerabilityIdsByDefinitionId = await dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(item => item.TenantId == tenantSoftware.TenantId)
            .ToDictionaryAsync(item => item.VulnerabilityDefinitionId, item => item.Id, ct);

        var activeInstallations = await dbContext
            .NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item => item.TenantSoftwareId == id && item.IsActive)
            .ToListAsync(ct);
        var relevantSoftwareAssetIds = activeInstallations
            .Select(item => item.SoftwareAssetId)
            .Distinct()
            .ToList();

        var openMatches = await dbContext
            .SoftwareVulnerabilityMatches.AsNoTracking()
            .Where(match => relevantSoftwareAssetIds.Contains(match.SoftwareAssetId))
            .Select(match => new { match.SoftwareAssetId, match.VulnerabilityDefinitionId, match.ResolvedAt })
            .ToListAsync(ct);

        return Ok(
            projections
                .Select(item =>
                {
                    var relatedSoftwareAssetIds = openMatches
                        .Where(match => match.VulnerabilityDefinitionId == item.projection.VulnerabilityDefinitionId)
                        .Where(match =>
                            item.projection.ResolvedAt is null || match.ResolvedAt is null
                        )
                        .Select(match => match.SoftwareAssetId)
                        .ToHashSet();
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

                    var tenantVulnerabilityId = tenantVulnerabilityIdsByDefinitionId[
                        item.projection.VulnerabilityDefinitionId
                    ];

                    return new TenantSoftwareVulnerabilityDto(
                        tenantVulnerabilityId,
                        item.vulnerabilityDefinition.Id,
                        item.vulnerabilityDefinition.ExternalId,
                        item.vulnerabilityDefinition.Title,
                        item.vulnerabilityDefinition.VendorSeverity.ToString(),
                        item.vulnerabilityDefinition.CvssScore,
                        item.vulnerabilityDefinition.PublishedDate,
                        item.vulnerabilityDefinition.Source,
                        item.projection.BestMatchMethod.ToString(),
                        item.projection.BestConfidence.ToString(),
                        item.projection.AffectedInstallCount,
                        item.projection.AffectedDeviceCount,
                        item.projection.AffectedVersionCount,
                        affectedVersions,
                        item.projection.FirstSeenAt,
                        item.projection.LastSeenAt,
                        item.projection.ResolvedAt,
                        ParseEvidence(item.projection.EvidenceJson)
                    );
                })
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

        var tenantSoftware = await dbContext
            .TenantSoftware.AsNoTracking()
            .Where(item => item.Id == id && item.TenantId == currentTenantId)
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
            .Where(item => item.TenantSoftwareId == id)
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

        var projections = await dbContext
            .NormalizedSoftwareVulnerabilityProjections.AsNoTracking()
            .Where(item => item.TenantSoftwareId == id)
            .Join(
                dbContext.VulnerabilityDefinitions.AsNoTracking(),
                projection => projection.VulnerabilityDefinitionId,
                vulnerabilityDefinition => vulnerabilityDefinition.Id,
                (projection, vulnerabilityDefinition) => new { projection, vulnerabilityDefinition }
            )
            .OrderByDescending(item => item.vulnerabilityDefinition.CvssScore)
            .ThenByDescending(item => item.vulnerabilityDefinition.PublishedDate)
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

        var matches = await dbContext
            .SoftwareVulnerabilityMatches.AsNoTracking()
            .Where(match => relevantSoftwareAssetIds.Contains(match.SoftwareAssetId))
            .Select(match => new
            {
                match.SoftwareAssetId,
                match.VulnerabilityDefinitionId,
                match.ResolvedAt,
                Method = match.MatchMethod.ToString(),
                Confidence = match.Confidence.ToString(),
                match.Evidence,
                match.FirstSeenAt,
                match.LastSeenAt,
            })
            .ToListAsync(ct);

        var vulnerabilityPayload = projections.Select(item =>
        {
            var relatedMatches = matches
                .Where(match => match.VulnerabilityDefinitionId == item.projection.VulnerabilityDefinitionId)
                .Where(match => item.projection.ResolvedAt is null || match.ResolvedAt is null)
                .ToList();
            var relatedSoftwareAssetIds = relatedMatches.Select(match => match.SoftwareAssetId).ToHashSet();
            var affectedVersions = activeInstallations
                .Where(installation => relatedSoftwareAssetIds.Contains(installation.SoftwareAssetId))
                .Select(installation => installation.DetectedVersion)
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(version => version, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new
            {
                TenantVulnerabilityId = tenantVulnerabilityIdsByDefinitionId[item.projection.VulnerabilityDefinitionId],
                item.vulnerabilityDefinition.Id,
                item.vulnerabilityDefinition.ExternalId,
                item.vulnerabilityDefinition.Title,
                VendorSeverity = item.vulnerabilityDefinition.VendorSeverity.ToString(),
                item.vulnerabilityDefinition.CvssScore,
                item.vulnerabilityDefinition.PublishedDate,
                item.vulnerabilityDefinition.Source,
                BestMatchMethod = item.projection.BestMatchMethod.ToString(),
                BestConfidence = item.projection.BestConfidence.ToString(),
                item.projection.AffectedInstallCount,
                item.projection.AffectedDeviceCount,
                item.projection.AffectedVersionCount,
                AffectedVersions = affectedVersions,
                item.projection.FirstSeenAt,
                item.projection.LastSeenAt,
                item.projection.ResolvedAt,
                Evidence = relatedMatches.Select(match => new
                {
                    match.Method,
                    match.Confidence,
                    match.Evidence,
                    match.FirstSeenAt,
                    match.LastSeenAt,
                    match.ResolvedAt,
                }),
            };
        });

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
                UniqueDeviceCount = activeInstallations.Select(item => item.DeviceAssetId).Distinct().Count(),
                VulnerableInstallCount = activeInstallations.Count(item =>
                    matches.Any(match => match.SoftwareAssetId == item.SoftwareAssetId && match.ResolvedAt == null)
                ),
                ActiveVulnerabilityCount = projections.Count(item => item.projection.ResolvedAt == null),
            },
            aliases,
            installations = installations.Select(item => new
            {
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
            }),
            vulnerabilities = vulnerabilityPayload,
        };

        var generationResult = await tenantAiTextGenerationService.GenerateAsync(
            tenantSoftware.TenantId,
            request.TenantAiProfileId,
            new AiTextGenerationRequest(
                "You are a PatchHound software exposure analyst. Use only the provided JSON. " +
                "Summarize prevalence, versions, linked vulnerability exposure, remediation priorities, and any notable operational concentration. " +
                "Return markdown with these sections: Executive Summary, Exposure Surface, Vulnerability Landscape, Priority Actions.",
                JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })
            ),
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
}
