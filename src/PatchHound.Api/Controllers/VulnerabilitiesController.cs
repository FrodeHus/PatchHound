using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Vulnerabilities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/vulnerabilities")]
[Authorize]
public class VulnerabilitiesController : ControllerBase
{
    private sealed record VulnerabilityEpisodeHistoryRow(
        Guid AssetId,
        int EpisodeNumber,
        VulnerabilityStatus Status,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset LastSeenAt,
        DateTimeOffset? ResolvedAt
    );

    private sealed record SoftwareCorrelationRow(
        Guid DeviceAssetId,
        string Name,
        int EpisodeNumber,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset? RemovedAt
    );

    private readonly PatchHoundDbContext _dbContext;
    private readonly VulnerabilityService _vulnerabilityService;
    private readonly AiReportService _aiReportService;
    private readonly ITenantContext _tenantContext;

    public VulnerabilitiesController(
        PatchHoundDbContext dbContext,
        VulnerabilityService vulnerabilityService,
        AiReportService aiReportService,
        ITenantContext tenantContext
    )
    {
        _dbContext = dbContext;
        _vulnerabilityService = vulnerabilityService;
        _aiReportService = aiReportService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<VulnerabilityDto>>> List(
        [FromQuery] VulnerabilityFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        var activeSnapshotId = await ResolveActiveVulnerabilitySnapshotIdAsync(currentTenantId, ct);

        var query = _dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(v => v.TenantId == currentTenantId)
            .AsQueryable();

        if (
            !string.IsNullOrEmpty(filter.Severity)
            && Enum.TryParse<Severity>(filter.Severity, out var severity)
        )
            query = query.Where(v => v.VulnerabilityDefinition.VendorSeverity == severity);
        if (
            !string.IsNullOrEmpty(filter.Status)
            && Enum.TryParse<VulnerabilityStatus>(filter.Status, out var status)
        )
            query = query.Where(v => v.Status == status);
        if (!string.IsNullOrEmpty(filter.Source))
            query = query.Where(v => v.VulnerabilityDefinition.Source.Contains(filter.Source));
        if (!string.IsNullOrEmpty(filter.Search))
            query = query.Where(v =>
                v.VulnerabilityDefinition.Title.Contains(filter.Search)
                || v.VulnerabilityDefinition.ExternalId.Contains(filter.Search)
            );
        if (filter.TenantId.HasValue)
        {
            if (!_tenantContext.HasAccessToTenant(filter.TenantId.Value))
                return Forbid();
            query = query.Where(v => v.TenantId == filter.TenantId.Value);
        }
        if (filter.PresentOnly != false)
        {
            query = query.Where(v => v.Status == VulnerabilityStatus.Open);
        }
        if (filter.RecurrenceOnly == true)
        {
            var recurringTenantVulnerabilityIds = await _dbContext
                .VulnerabilityAssetEpisodes.AsNoTracking()
                .Where(episode => episode.TenantId == currentTenantId)
                .GroupBy(episode => new { episode.TenantVulnerabilityId, episode.AssetId })
                .Where(group => group.Count() > 1)
                .Select(group => group.Key.TenantVulnerabilityId)
                .Distinct()
                .ToListAsync(ct);
            query = query.Where(v => recurringTenantVulnerabilityIds.Contains(v.Id));
        }

        var totalCount = await query.CountAsync(ct);

        var tenantVulnerabilityIds = await query
            .OrderByDescending(v => v.VulnerabilityDefinition.CvssScore)
            .ThenByDescending(v => v.VulnerabilityDefinition.PublishedDate)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(v => v.Id)
            .ToListAsync(ct);

        var episodeRows = await _dbContext
            .VulnerabilityAssetEpisodes.AsNoTracking()
            .Where(episode => tenantVulnerabilityIds.Contains(episode.TenantVulnerabilityId))
            .Select(episode => new
            {
                episode.TenantVulnerabilityId,
                episode.AssetId,
                episode.EpisodeNumber,
                episode.FirstSeenAt,
            })
            .ToListAsync(ct);

        var recentReappearanceThreshold = DateTimeOffset.UtcNow.AddDays(-30);
        var episodeCountsByVulnerabilityId = episodeRows
            .GroupBy(episode => episode.TenantVulnerabilityId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var reappearanceEpisodes = group
                        .GroupBy(episode => episode.AssetId)
                        .SelectMany(assetEpisodes =>
                            assetEpisodes.Where(episode => episode.EpisodeNumber > 1)
                        )
                        .ToList();

                    return new
                    {
                        EpisodeCount = group.Count(),
                        ReappearanceCount = reappearanceEpisodes.Count,
                        HasRecentReappearance = reappearanceEpisodes.Any(episode =>
                            episode.FirstSeenAt >= recentReappearanceThreshold
                        ),
                    };
                }
            );

        var itemRows = await query
            .OrderByDescending(v => v.VulnerabilityDefinition.CvssScore)
            .ThenByDescending(v => v.VulnerabilityDefinition.PublishedDate)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(v => new
            {
                v.Id,
                v.VulnerabilityDefinition.ExternalId,
                Title = v.VulnerabilityDefinition.Title,
                VendorSeverity = v.VulnerabilityDefinition.VendorSeverity.ToString(),
                Status = v.Status.ToString(),
                Source = v.VulnerabilityDefinition.Source,
                CvssScore = v.VulnerabilityDefinition.CvssScore,
                PublishedDate = v.VulnerabilityDefinition.PublishedDate,
                AffectedAssetCount = _dbContext
                    .VulnerabilityAssets.Where(link =>
                        link.TenantVulnerabilityId == v.Id && link.SnapshotId == activeSnapshotId
                    )
                    .Count(),
                AdjustedSeverity = _dbContext
                    .OrganizationalSeverities.Where(os => os.TenantVulnerabilityId == v.Id)
                    .OrderByDescending(os => os.AdjustedAt)
                    .Select(os => os.AdjustedSeverity.ToString())
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var items = itemRows
            .Select(v => new VulnerabilityDto(
                v.Id,
                v.ExternalId,
                v.Title,
                v.VendorSeverity,
                v.Status,
                FormatSourceDisplay(v.Source),
                v.CvssScore,
                v.PublishedDate,
                v.AffectedAssetCount,
                v.AdjustedSeverity,
                episodeCountsByVulnerabilityId.TryGetValue(v.Id, out var episodeInfo)
                    ? episodeInfo.EpisodeCount
                    : 0,
                episodeCountsByVulnerabilityId.TryGetValue(v.Id, out episodeInfo)
                    ? episodeInfo.ReappearanceCount
                    : 0,
                episodeCountsByVulnerabilityId.TryGetValue(v.Id, out episodeInfo)
                    && episodeInfo.HasRecentReappearance
            ))
            .ToList();

        return Ok(
            new PagedResponse<VulnerabilityDto>(
                items,
                totalCount,
                pagination.Page,
                pagination.BoundedPageSize
            )
        );
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<VulnerabilityDetailDto>> Get(Guid id, CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        var activeSnapshotId = await ResolveActiveVulnerabilitySnapshotIdAsync(currentTenantId, ct);

        var tenantVulnerability = await _dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Include(tv => tv.VulnerabilityDefinition)
            .ThenInclude(definition => definition.AffectedSoftware)
            .Include(tv => tv.VulnerabilityDefinition)
            .ThenInclude(definition => definition.References)
            .FirstOrDefaultAsync(tv => tv.Id == id && tv.TenantId == currentTenantId, ct);

        if (tenantVulnerability is null)
            return NotFound();
        var definition = tenantVulnerability.VulnerabilityDefinition;

        var orgSeverity = await _dbContext
            .OrganizationalSeverities.AsNoTracking()
            .Where(os => os.TenantVulnerabilityId == tenantVulnerability.Id)
            .OrderByDescending(os => os.AdjustedAt)
            .FirstOrDefaultAsync(ct);

        var vulnerabilityAssets = await _dbContext
            .VulnerabilityAssets.AsNoTracking()
            .Where(link =>
                link.TenantVulnerabilityId == tenantVulnerability.Id
                && link.SnapshotId == activeSnapshotId
            )
            .ToListAsync(ct);

        // Get asset names for affected assets
        var assetIds = vulnerabilityAssets.Select(a => a.AssetId).ToList();
        var assets = await _dbContext
            .Assets.AsNoTracking()
            .Where(a => assetIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);
        var securityProfileIds = assets
            .Values.Where(asset => asset.SecurityProfileId.HasValue)
            .Select(asset => asset.SecurityProfileId!.Value)
            .Distinct()
            .ToList();
        var securityProfileNamesById = await _dbContext
            .AssetSecurityProfiles.AsNoTracking()
            .Where(profile => securityProfileIds.Contains(profile.Id))
            .ToDictionaryAsync(profile => profile.Id, profile => profile.Name, ct);

        var assessmentsByAssetId = await _dbContext
            .VulnerabilityAssetAssessments.AsNoTracking()
            .Where(assessment =>
                assessment.TenantVulnerabilityId == tenantVulnerability.Id
                && assessment.SnapshotId == activeSnapshotId
            )
            .ToDictionaryAsync(assessment => assessment.AssetId, ct);

        var episodeRows = await _dbContext
            .VulnerabilityAssetEpisodes.AsNoTracking()
            .Where(episode => episode.TenantVulnerabilityId == tenantVulnerability.Id)
            .OrderBy(episode => episode.AssetId)
            .ThenBy(episode => episode.EpisodeNumber)
            .Select(episode => new VulnerabilityEpisodeHistoryRow(
                episode.AssetId,
                episode.EpisodeNumber,
                episode.Status,
                episode.FirstSeenAt,
                episode.LastSeenAt,
                episode.ResolvedAt
            ))
            .ToListAsync(ct);

        var episodesByAssetId = episodeRows
            .GroupBy(row => row.AssetId)
            .ToDictionary(
                group => group.Key,
                group =>
                    group
                        .Select(row => new VulnerabilityEpisodeDto(
                            row.EpisodeNumber,
                            row.Status.ToString(),
                            row.FirstSeenAt,
                            row.LastSeenAt,
                            row.ResolvedAt
                        ))
                        .ToList() as IReadOnlyList<VulnerabilityEpisodeDto>
            );

        var softwareEpisodeRows = await _dbContext
            .DeviceSoftwareInstallationEpisodes.AsNoTracking()
            .Where(episode => assetIds.Contains(episode.DeviceAssetId))
            .Join(
                _dbContext.Assets.AsNoTracking(),
                episode => episode.SoftwareAssetId,
                software => software.Id,
                (episode, software) =>
                    new SoftwareCorrelationRow(
                        episode.DeviceAssetId,
                        software.Name,
                        episode.EpisodeNumber,
                        episode.FirstSeenAt,
                        episode.RemovedAt
                    )
            )
            .ToListAsync(ct);

        var matchedSoftwareRows = await _dbContext
            .SoftwareVulnerabilityMatches.AsNoTracking()
            .Where(match => match.VulnerabilityDefinitionId == tenantVulnerability.VulnerabilityDefinitionId && match.ResolvedAt == null)
            .Join(
                _dbContext.Assets.AsNoTracking(),
                match => match.SoftwareAssetId,
                software => software.Id,
                (match, software) =>
                    new
                    {
                        software.Id,
                        software.Name,
                        software.ExternalId,
                        MatchMethod = match.MatchMethod.ToString(),
                        Confidence = match.Confidence.ToString(),
                        match.Evidence,
                        match.FirstSeenAt,
                        match.LastSeenAt,
                        match.ResolvedAt,
                    }
            )
            .OrderBy(item => item.Name)
            .ToListAsync(ct);

        var matchedSoftwareExternalIds = matchedSoftwareRows
            .Select(item => item.ExternalId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var tenantSoftwareIdsByExternalId = matchedSoftwareExternalIds.Count == 0
            ? new Dictionary<string, Guid?>(StringComparer.Ordinal)
            : await _dbContext
                .TenantSoftware.AsNoTracking()
                .Join(
                    _dbContext.NormalizedSoftwareAliases.AsNoTracking(),
                    tenantSoftware => tenantSoftware.NormalizedSoftwareId,
                    alias => alias.NormalizedSoftwareId,
                    (tenantSoftware, alias) => new
                    {
                        tenantSoftware.Id,
                        tenantSoftware.TenantId,
                        alias.SourceSystem,
                        alias.ExternalSoftwareId,
                    }
                )
                .Where(item =>
                    item.TenantId == tenantVulnerability.TenantId
                    && item.SourceSystem == SoftwareIdentitySourceSystem.Defender
                    && matchedSoftwareExternalIds.Contains(item.ExternalSoftwareId)
                )
                .GroupBy(item => item.ExternalSoftwareId)
                .Select(group => new { ExternalSoftwareId = group.Key, TenantSoftwareId = group.Select(item => item.Id).First() })
                .ToDictionaryAsync(
                    item => item.ExternalSoftwareId,
                    item => (Guid?)item.TenantSoftwareId,
                    ct
                );

        var matchedSoftware = matchedSoftwareRows
            .Select(item => new MatchedSoftwareDto(
                item.Id,
                tenantSoftwareIdsByExternalId.TryGetValue(item.ExternalId, out var tenantSoftwareId)
                    ? tenantSoftwareId
                    : null,
                item.Name,
                item.ExternalId,
                item.MatchMethod,
                item.Confidence,
                item.Evidence,
                item.FirstSeenAt,
                item.LastSeenAt,
                item.ResolvedAt
            ))
            .ToList();

        var tenantHistory = BuildTenantHistory(episodeRows);

        var detail = new VulnerabilityDetailDto(
            tenantVulnerability.Id,
            definition.ExternalId,
            definition.Title,
            definition.Description,
            definition.VendorSeverity.ToString(),
            tenantVulnerability.Status.ToString(),
            FormatSourceDisplay(definition.Source),
            definition.GetSources(),
            definition.CvssScore,
            definition.CvssVector,
            definition.PublishedDate,
            definition
                .AffectedSoftware.OrderBy(item => item.Criteria)
                .Select(item => new VulnerabilityAffectedSoftwareDto(
                    item.Vulnerable,
                    item.Criteria,
                    item.VersionStartIncluding,
                    item.VersionStartExcluding,
                    item.VersionEndIncluding,
                    item.VersionEndExcluding
                ))
                .ToList(),
            matchedSoftware,
            definition
                .References.OrderBy(reference => reference.Source)
                .ThenBy(reference => reference.Url)
                .Select(reference => new VulnerabilityReferenceDto(
                    reference.Url,
                    reference.Source,
                    reference.GetTags()
                ))
                .ToList(),
            tenantHistory,
            vulnerabilityAssets
                .Select(va =>
                {
                    assets.TryGetValue(va.AssetId, out var asset);
                    assessmentsByAssetId.TryGetValue(va.AssetId, out var assessment);
                    episodesByAssetId.TryGetValue(va.AssetId, out var episodeHistory);

                    return new AffectedAssetDto(
                        va.AssetId,
                        asset?.Name ?? "Unknown",
                        asset?.AssetType.ToString() ?? "Unknown",
                        asset?.SecurityProfileId is Guid profileId
                        && securityProfileNamesById.TryGetValue(profileId, out var profileName)
                            ? profileName
                            : null,
                        va.Status.ToString(),
                        definition.VendorSeverity.ToString(),
                        assessment?.BaseScore ?? definition.CvssScore,
                        assessment?.EffectiveSeverity.ToString()
                            ?? definition.VendorSeverity.ToString(),
                        assessment?.EffectiveScore ?? definition.CvssScore,
                        assessment?.ReasonSummary,
                        va.DetectedDate,
                        va.ResolvedDate,
                        episodeHistory?.Count ?? 0,
                        episodeHistory ?? [],
                        GetPossibleCorrelatedSoftware(
                            softwareEpisodeRows
                                .Where(row => row.DeviceAssetId == va.AssetId)
                                .ToList(),
                            episodeHistory ?? []
                        )
                    );
                })
                .ToList(),
            orgSeverity is null
                ? null
                : new OrganizationalSeverityDto(
                    orgSeverity.AdjustedSeverity.ToString(),
                    orgSeverity.Justification,
                    orgSeverity.AssetCriticalityFactor,
                    orgSeverity.ExposureFactor,
                    orgSeverity.CompensatingControls,
                    orgSeverity.AdjustedAt
                )
        );

        return Ok(detail);
    }

    private async Task<Guid?> ResolveActiveVulnerabilitySnapshotIdAsync(
        Guid tenantId,
        CancellationToken ct
    )
    {
        return await _dbContext
            .TenantSourceConfigurations.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId && item.SourceKey == TenantSourceCatalog.DefenderSourceKey
            )
            .Select(item => item.ActiveSnapshotId)
            .FirstOrDefaultAsync(ct);
    }

    private static string FormatSourceDisplay(string source)
    {
        return string.Join(
            ", ",
            source.Split(
                '|',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        );
    }

    private static VulnerabilityTenantHistoryDto BuildTenantHistory(
        IReadOnlyList<VulnerabilityEpisodeHistoryRow> episodeRows
    )
    {
        if (episodeRows.Count == 0)
        {
            return new VulnerabilityTenantHistoryDto(null, null, null, null, false, 0, 0, 0);
        }

        var events = episodeRows
            .SelectMany(row =>
            {
                var list = new List<(DateTimeOffset Timestamp, int Delta)>
                {
                    (row.FirstSeenAt, +1),
                };

                if (row.ResolvedAt is DateTimeOffset resolvedAt)
                {
                    list.Add((resolvedAt, -1));
                }

                return list;
            })
            .OrderBy(item => item.Timestamp)
            .ThenBy(item => item.Delta)
            .ToList();

        var openCount = 0;
        DateTimeOffset? firstSeenAt = null;
        DateTimeOffset? lastGoneAt = null;
        DateTimeOffset? lastReappearedAt = null;
        var reappearanceCount = 0;

        foreach (var current in events)
        {
            var wasOpen = openCount > 0;
            openCount += current.Delta;
            var isOpen = openCount > 0;

            if (!wasOpen && isOpen)
            {
                if (firstSeenAt is null)
                {
                    firstSeenAt = current.Timestamp;
                }
                else
                {
                    lastReappearedAt = current.Timestamp;
                    reappearanceCount++;
                }
            }

            if (wasOpen && !isOpen)
            {
                lastGoneAt = current.Timestamp;
            }
        }

        return new VulnerabilityTenantHistoryDto(
            firstSeenAt,
            episodeRows.Max(row => (DateTimeOffset)row.LastSeenAt),
            lastGoneAt,
            lastReappearedAt,
            episodeRows.Any(row => row.Status == VulnerabilityStatus.Open),
            episodeRows.Count(row => row.Status == VulnerabilityStatus.Open),
            episodeRows.Count,
            reappearanceCount
        );
    }

    private static IReadOnlyList<string> GetPossibleCorrelatedSoftware(
        IReadOnlyList<SoftwareCorrelationRow> softwareRows,
        IReadOnlyList<VulnerabilityEpisodeDto> episodes
    )
    {
        return softwareRows
            .Select(softwareRow =>
            {
                var matchingEpisodes = episodes
                    .Where(episode =>
                        softwareRow.FirstSeenAt <= episode.FirstSeenAt
                        && (
                            softwareRow.RemovedAt is null
                            || softwareRow.RemovedAt >= episode.FirstSeenAt
                        )
                    )
                    .Select(episode =>
                    {
                        var age = episode.FirstSeenAt - softwareRow.FirstSeenAt;
                        var score = 0;

                        if (softwareRow.EpisodeNumber > 1)
                        {
                            score += 200;
                        }

                        if (episode.EpisodeNumber > 1)
                        {
                            score += 100;
                        }

                        score += age.TotalDays switch
                        {
                            <= 1 => 80,
                            <= 7 => 50,
                            <= 30 => 20,
                            _ => 0,
                        };

                        return new
                        {
                            softwareRow.Name,
                            Score = score,
                            Age = age,
                        };
                    })
                    .OrderByDescending(item => item.Score)
                    .ThenBy(item => item.Age)
                    .FirstOrDefault();

                return matchingEpisodes;
            })
            .Where(item => item is not null)
            .GroupBy(item => item!.Name, StringComparer.Ordinal)
            .Select(group =>
                group.OrderByDescending(item => item!.Score).ThenBy(item => item!.Age).First()
            )
            .OrderByDescending(item => item!.Score)
            .ThenBy(item => item!.Age)
            .Select(item => item!.Name)
            .ToList();
    }

    [HttpPut("{id:guid}/organizational-severity")]
    [Authorize(Policy = Policies.AdjustSeverity)]
    public async Task<IActionResult> UpdateOrganizationalSeverity(
        Guid id,
        [FromBody] UpdateOrgSeverityRequest request,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<Severity>(request.AdjustedSeverity, out var severity))
            return BadRequest(new ProblemDetails { Title = "Invalid severity value" });

        if (_tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var tenantVulnerabilityId = await _dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Where(item => item.Id == id && item.TenantId == currentTenantId)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(ct);

        if (!tenantVulnerabilityId.HasValue)
            return NotFound(new ProblemDetails { Title = "Tenant vulnerability not found" });

        var result = await _vulnerabilityService.UpdateOrganizationalSeverityAsync(
            tenantVulnerabilityId.Value,
            severity,
            request.Justification,
            request.AssetCriticalityFactor,
            request.ExposureFactor,
            request.CompensatingControls,
            ct
        );

        if (!result.IsSuccess)
            return NotFound(new ProblemDetails { Title = result.Error });

        return NoContent();
    }

    [HttpPost("{id:guid}/ai-report")]
    [Authorize(Policy = Policies.GenerateAiReports)]
    public async Task<ActionResult<AiReportDto>> GenerateAiReport(
        Guid id,
        [FromBody] GenerateAiReportRequest request,
        CancellationToken ct
    )
    {
        var tenantVulnerability = await _dbContext
            .TenantVulnerabilities.AsNoTracking()
            .Include(item => item.VulnerabilityDefinition)
            .FirstOrDefaultAsync(item => item.Id == id, ct);

        if (tenantVulnerability is null)
            return NotFound(new ProblemDetails { Title = "Tenant vulnerability not found" });

        if (!_tenantContext.HasAccessToTenant(tenantVulnerability.TenantId))
            return Forbid();

        var assetIds = await _dbContext
            .VulnerabilityAssets.AsNoTracking()
            .Where(item => item.TenantVulnerabilityId == tenantVulnerability.Id)
            .Select(item => item.AssetId)
            .ToListAsync(ct);
        var affectedAssets = await _dbContext
            .Assets.AsNoTracking()
            .Where(a => assetIds.Contains(a.Id))
            .ToListAsync(ct);

        var result = await _aiReportService.GenerateReportAsync(
            tenantVulnerability.VulnerabilityDefinition,
            tenantVulnerability.Id,
            affectedAssets,
            tenantVulnerability.TenantId,
            _tenantContext.CurrentUserId,
            request.TenantAiProfileId,
            ct
        );

        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        var report = result.Value;
        _dbContext.AIReports.Add(report);
        await _dbContext.SaveChangesAsync(ct);

        return Ok(
            new AiReportDto(
                report.Id,
                report.TenantVulnerabilityId,
                report.Content,
                report.ProviderType,
                report.ProfileName,
                report.Model,
                report.GeneratedAt
            )
        );
    }
}
