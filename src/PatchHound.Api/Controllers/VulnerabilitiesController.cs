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
        var query = _dbContext.Vulnerabilities.AsNoTracking().AsQueryable();

        if (
            !string.IsNullOrEmpty(filter.Severity)
            && Enum.TryParse<Severity>(filter.Severity, out var severity)
        )
            query = query.Where(v => v.VendorSeverity == severity);
        if (
            !string.IsNullOrEmpty(filter.Status)
            && Enum.TryParse<VulnerabilityStatus>(filter.Status, out var status)
        )
            query = query.Where(v => v.Status == status);
        if (!string.IsNullOrEmpty(filter.Source))
            query = query.Where(v => v.Source == filter.Source);
        if (!string.IsNullOrEmpty(filter.Search))
            query = query.Where(v =>
                v.Title.Contains(filter.Search) || v.ExternalId.Contains(filter.Search)
            );
        if (filter.TenantId.HasValue)
        {
            if (!_tenantContext.HasAccessToTenant(filter.TenantId.Value))
                return Forbid();
            query = query.Where(v => v.TenantId == filter.TenantId.Value);
        }
        if (filter.RecurrenceOnly == true)
        {
            query = query.Where(v =>
                _dbContext.VulnerabilityAssetEpisodes.Count(episode => episode.VulnerabilityId == v.Id)
                > _dbContext.VulnerabilityAssetEpisodes
                    .Where(episode => episode.VulnerabilityId == v.Id)
                    .Select(episode => episode.AssetId)
                    .Distinct()
                    .Count()
            );
        }

        var totalCount = await query.CountAsync(ct);

        var vulnerabilityIds = await query
            .OrderByDescending(v => v.CvssScore)
            .ThenByDescending(v => v.PublishedDate)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(v => v.Id)
            .ToListAsync(ct);

        var episodeRows = await _dbContext
            .VulnerabilityAssetEpisodes.AsNoTracking()
            .Where(episode => vulnerabilityIds.Contains(episode.VulnerabilityId))
            .ToListAsync(ct);

        var recentReappearanceThreshold = DateTimeOffset.UtcNow.AddDays(-30);
        var episodeCountsByVulnerabilityId = episodeRows
            .GroupBy(episode => episode.VulnerabilityId)
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
            .OrderByDescending(v => v.CvssScore)
            .ThenByDescending(v => v.PublishedDate)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(v => new
            {
                v.Id,
                v.ExternalId,
                v.Title,
                VendorSeverity = v.VendorSeverity.ToString(),
                Status = v.Status.ToString(),
                v.Source,
                v.CvssScore,
                v.PublishedDate,
                AffectedAssetCount = v.AffectedAssets.Count,
                AdjustedSeverity = _dbContext
                    .OrganizationalSeverities.Where(os => os.VulnerabilityId == v.Id)
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
                v.Source,
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

        return Ok(new PagedResponse<VulnerabilityDto>(items, totalCount));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<VulnerabilityDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var vulnerability = await _dbContext
            .Vulnerabilities.AsNoTracking()
            .Include(v => v.AffectedAssets)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

        if (vulnerability is null)
            return NotFound();

        var orgSeverity = await _dbContext
            .OrganizationalSeverities.AsNoTracking()
            .Where(os => os.VulnerabilityId == id)
            .OrderByDescending(os => os.AdjustedAt)
            .FirstOrDefaultAsync(ct);

        // Get asset names for affected assets
        var assetIds = vulnerability.AffectedAssets.Select(a => a.AssetId).ToList();
        var assets = await _dbContext
            .Assets.AsNoTracking()
            .Where(a => assetIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);
        var securityProfileIds = assets.Values
            .Where(asset => asset.SecurityProfileId.HasValue)
            .Select(asset => asset.SecurityProfileId!.Value)
            .Distinct()
            .ToList();
        var securityProfileNamesById = await _dbContext
            .AssetSecurityProfiles.AsNoTracking()
            .Where(profile => securityProfileIds.Contains(profile.Id))
            .ToDictionaryAsync(profile => profile.Id, profile => profile.Name, ct);

        var assessmentsByAssetId = await _dbContext
            .VulnerabilityAssetAssessments.AsNoTracking()
            .Where(assessment => assessment.VulnerabilityId == id)
            .ToDictionaryAsync(assessment => assessment.AssetId, ct);

        var episodeRows = await _dbContext
            .VulnerabilityAssetEpisodes.AsNoTracking()
            .Where(episode => episode.VulnerabilityId == id)
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
                group => group
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
                (episode, software) => new SoftwareCorrelationRow(
                    episode.DeviceAssetId,
                    software.Name,
                    episode.EpisodeNumber,
                    episode.FirstSeenAt,
                    episode.RemovedAt
                )
            )
            .ToListAsync(ct);

        var tenantHistory = BuildTenantHistory(episodeRows);

        var detail = new VulnerabilityDetailDto(
            vulnerability.Id,
            vulnerability.ExternalId,
            vulnerability.Title,
            vulnerability.Description,
            vulnerability.VendorSeverity.ToString(),
            vulnerability.Status.ToString(),
            vulnerability.Source,
            vulnerability.CvssScore,
            vulnerability.CvssVector,
            vulnerability.PublishedDate,
            tenantHistory,
            vulnerability
                .AffectedAssets.Select(va =>
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
                        vulnerability.VendorSeverity.ToString(),
                        assessment?.BaseScore ?? vulnerability.CvssScore,
                        assessment?.EffectiveSeverity.ToString() ?? vulnerability.VendorSeverity.ToString(),
                        assessment?.EffectiveScore ?? vulnerability.CvssScore,
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

    private static VulnerabilityTenantHistoryDto BuildTenantHistory(
        IReadOnlyList<VulnerabilityEpisodeHistoryRow> episodeRows
    )
    {
        if (episodeRows.Count == 0)
        {
            return new VulnerabilityTenantHistoryDto(
                null,
                null,
                null,
                null,
                false,
                0,
                0,
                0
            );
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
                        && (softwareRow.RemovedAt is null
                            || softwareRow.RemovedAt >= episode.FirstSeenAt)
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
            .Select(group => group
                .OrderByDescending(item => item!.Score)
                .ThenBy(item => item!.Age)
                .First())
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

        var result = await _vulnerabilityService.UpdateOrganizationalSeverityAsync(
            id,
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
        var vulnerability = await _dbContext
            .Vulnerabilities.AsNoTracking()
            .Include(v => v.AffectedAssets)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

        if (vulnerability is null)
            return NotFound(new ProblemDetails { Title = "Vulnerability not found" });

        if (!_tenantContext.HasAccessToTenant(vulnerability.TenantId))
            return Forbid();

        var assetIds = vulnerability.AffectedAssets.Select(a => a.AssetId).ToList();
        var affectedAssets = await _dbContext
            .Assets.AsNoTracking()
            .Where(a => assetIds.Contains(a.Id))
            .ToListAsync(ct);

        var result = await _aiReportService.GenerateReportAsync(
            vulnerability,
            affectedAssets,
            vulnerability.TenantId,
            _tenantContext.CurrentUserId,
            request.ProviderName,
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
                report.VulnerabilityId,
                report.Content,
                report.Provider,
                report.GeneratedAt
            )
        );
    }
}
