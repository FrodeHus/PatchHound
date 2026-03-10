using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Software;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/software")]
[Authorize]
public class SoftwareController(PatchHoundDbContext dbContext) : ControllerBase
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
    public async Task<ActionResult<NormalizedSoftwareDetailDto>> Get(
        Guid id,
        CancellationToken ct
    )
    {
        var software = await dbContext
            .NormalizedSoftware.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, ct);
        if (software is null)
        {
            return NotFound();
        }

        var aliases = await dbContext
            .NormalizedSoftwareAliases.AsNoTracking()
            .Where(item => item.NormalizedSoftwareId == id)
            .OrderBy(item => item.SourceSystem)
            .ThenBy(item => item.ExternalSoftwareId)
            .ToListAsync(ct);

        var installations = await dbContext
            .NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item => item.NormalizedSoftwareId == id)
            .ToListAsync(ct);

        var activeInstallations = installations.Where(item => item.IsActive).ToList();
        var softwareAssetIds = installations.Select(item => item.SoftwareAssetId).Distinct().ToList();

        var openMatches = await dbContext
            .SoftwareVulnerabilityMatches.AsNoTracking()
            .Where(match => match.ResolvedAt == null && softwareAssetIds.Contains(match.SoftwareAssetId))
            .Select(match => new { match.SoftwareAssetId, match.VulnerabilityId })
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
                return new NormalizedSoftwareVersionCohortDto(
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
            new NormalizedSoftwareDetailDto(
                software.Id,
                software.CanonicalName,
                software.CanonicalVendor,
                software.PrimaryCpe23Uri,
                software.NormalizationMethod.ToString(),
                software.Confidence.ToString(),
                installations.Count == 0 ? null : installations.Min(item => item.FirstSeenAt),
                installations.Count == 0 ? null : installations.Max(item => item.LastSeenAt),
                activeInstallations.Count,
                activeInstallations.Select(item => item.DeviceAssetId).Distinct().Count(),
                activeInstallations.Count(item =>
                    openMatchSoftwareAssetIds.Contains(item.SoftwareAssetId)
                ),
                await dbContext
                    .NormalizedSoftwareVulnerabilityProjections.AsNoTracking()
                    .CountAsync(item => item.NormalizedSoftwareId == id && item.ResolvedAt == null, ct),
                versionCohorts.Count,
                versionCohorts,
                aliases
                    .Select(item => new NormalizedSoftwareSourceAliasDto(
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

    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<NormalizedSoftwareListItemDto>>> List(
        [FromQuery] NormalizedSoftwareFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var query = dbContext.NormalizedSoftware.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query = query.Where(item =>
                item.CanonicalName.Contains(filter.Search)
                || (item.CanonicalVendor != null && item.CanonicalVendor.Contains(filter.Search))
            );
        }

        if (!string.IsNullOrWhiteSpace(filter.Confidence))
        {
            query = query.Where(item => item.Confidence.ToString() == filter.Confidence);
        }

        if (filter.BoundOnly == true)
        {
            query = query.Where(item => item.PrimaryCpe23Uri != null && item.PrimaryCpe23Uri != "");
        }

        if (filter.VulnerableOnly == true)
        {
            query = query.Where(item =>
                dbContext.NormalizedSoftwareVulnerabilityProjections.Any(projection =>
                    projection.NormalizedSoftwareId == item.Id && projection.ResolvedAt == null
                )
            );
        }

        var totalCount = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(item => item.CanonicalName)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(item => new
            {
                item.Id,
                item.CanonicalName,
                item.CanonicalVendor,
                Confidence = item.Confidence.ToString(),
                NormalizationMethod = item.NormalizationMethod.ToString(),
                item.PrimaryCpe23Uri,
                ActiveInstallCount = dbContext
                    .NormalizedSoftwareInstallations
                    .Where(installation => installation.NormalizedSoftwareId == item.Id && installation.IsActive)
                    .Count(),
                UniqueDeviceCount = dbContext
                    .NormalizedSoftwareInstallations
                    .Where(installation => installation.NormalizedSoftwareId == item.Id && installation.IsActive)
                    .Select(installation => installation.DeviceAssetId)
                    .Distinct()
                    .Count(),
                ActiveVulnerabilityCount = dbContext
                    .NormalizedSoftwareVulnerabilityProjections
                    .Where(projection => projection.NormalizedSoftwareId == item.Id && projection.ResolvedAt == null)
                    .Count(),
                VersionCount = dbContext
                    .NormalizedSoftwareInstallations
                    .Where(installation => installation.NormalizedSoftwareId == item.Id && installation.IsActive)
                    .Select(installation => installation.DetectedVersion ?? string.Empty)
                    .Distinct()
                    .Count(version => version != string.Empty),
                LastSeenAt = dbContext
                    .NormalizedSoftwareInstallations
                    .Where(installation => installation.NormalizedSoftwareId == item.Id)
                    .Select(installation => (DateTimeOffset?)installation.LastSeenAt)
                    .Max(),
            })
            .ToListAsync(ct);

        return Ok(
            new PagedResponse<NormalizedSoftwareListItemDto>(
                rows
                    .Select(item => new NormalizedSoftwareListItemDto(
                        item.Id,
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
    public async Task<ActionResult<PagedResponse<NormalizedSoftwareInstallationDto>>> GetInstallations(
        Guid id,
        [FromQuery] NormalizedSoftwareInstallationQuery query,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var softwareExists = await dbContext
            .NormalizedSoftware.AsNoTracking()
            .AnyAsync(item => item.Id == id, ct);
        if (!softwareExists)
        {
            return NotFound();
        }

        var installationsQuery = dbContext
            .NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item => item.NormalizedSoftwareId == id);

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
            new PagedResponse<NormalizedSoftwareInstallationDto>(
                rows
                    .Select(item => new NormalizedSoftwareInstallationDto(
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
    public async Task<ActionResult<IReadOnlyList<NormalizedSoftwareVulnerabilityDto>>> GetVulnerabilities(
        Guid id,
        CancellationToken ct
    )
    {
        var softwareExists = await dbContext
            .NormalizedSoftware.AsNoTracking()
            .AnyAsync(item => item.Id == id, ct);
        if (!softwareExists)
        {
            return NotFound();
        }

        var projections = await dbContext
            .NormalizedSoftwareVulnerabilityProjections.AsNoTracking()
            .Where(item => item.NormalizedSoftwareId == id)
            .Join(
                dbContext.Vulnerabilities.AsNoTracking(),
                projection => projection.VulnerabilityId,
                vulnerability => vulnerability.Id,
                (projection, vulnerability) => new { projection, vulnerability }
            )
            .OrderByDescending(item => item.vulnerability.CvssScore)
            .ThenByDescending(item => item.vulnerability.PublishedDate)
            .ToListAsync(ct);

        var activeInstallations = await dbContext
            .NormalizedSoftwareInstallations.AsNoTracking()
            .Where(item => item.NormalizedSoftwareId == id && item.IsActive)
            .ToListAsync(ct);
        var relevantSoftwareAssetIds = activeInstallations
            .Select(item => item.SoftwareAssetId)
            .Distinct()
            .ToList();

        var openMatches = await dbContext
            .SoftwareVulnerabilityMatches.AsNoTracking()
            .Where(match => relevantSoftwareAssetIds.Contains(match.SoftwareAssetId))
            .Select(match => new { match.SoftwareAssetId, match.VulnerabilityId, match.ResolvedAt })
            .ToListAsync(ct);

        return Ok(
            projections
                .Select(item =>
                {
                    var relatedSoftwareAssetIds = openMatches
                        .Where(match => match.VulnerabilityId == item.projection.VulnerabilityId)
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

                    return new NormalizedSoftwareVulnerabilityDto(
                        item.vulnerability.Id,
                        item.vulnerability.ExternalId,
                        item.vulnerability.Title,
                        item.vulnerability.VendorSeverity.ToString(),
                        item.vulnerability.CvssScore,
                        item.vulnerability.PublishedDate,
                        item.vulnerability.Source,
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

    private static IReadOnlyList<NormalizedSoftwareVulnerabilityEvidenceDto> ParseEvidence(
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
                .Select(item => new NormalizedSoftwareVulnerabilityEvidenceDto(
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
