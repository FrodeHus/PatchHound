using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Assets;
using PatchHound.Api.Models.SecurityProfiles;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/assets")]
[Authorize]
public class AssetsController : ControllerBase
{
    private sealed record SoftwareCorrelationRow(
        Guid SoftwareAssetId,
        int EpisodeNumber,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset LastSeenAt,
        DateTimeOffset? RemovedAt
    );

    private sealed record ParsedCpeComponents(string Vendor, string Product, string? Version);

    private readonly PatchHoundDbContext _dbContext;
    private readonly AssetService _assetService;
    private readonly VulnerabilityAssessmentService _assessmentService;
    private readonly ITenantContext _tenantContext;

    public AssetsController(
        PatchHoundDbContext dbContext,
        AssetService assetService,
        VulnerabilityAssessmentService assessmentService,
        ITenantContext tenantContext
    )
    {
        _dbContext = dbContext;
        _assetService = assetService;
        _assessmentService = assessmentService;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<AssetDto>>> List(
        [FromQuery] AssetFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        var query = _dbContext.Assets.AsNoTracking().AsQueryable();

        if (
            !string.IsNullOrEmpty(filter.AssetType)
            && Enum.TryParse<AssetType>(filter.AssetType, out var assetType)
        )
            query = query.Where(a => a.AssetType == assetType);
        if (
            !string.IsNullOrEmpty(filter.Criticality)
            && Enum.TryParse<Criticality>(filter.Criticality, out var criticality)
        )
            query = query.Where(a => a.Criticality == criticality);
        if (
            !string.IsNullOrEmpty(filter.OwnerType)
            && Enum.TryParse<OwnerType>(filter.OwnerType, out var ownerType)
        )
            query = query.Where(a => a.OwnerType == ownerType);
        if (filter.UnassignedOnly == true)
            query = query.Where(a => a.OwnerUserId == null && a.OwnerTeamId == null);
        if (filter.OwnerId.HasValue)
            query = query.Where(a =>
                a.OwnerUserId == filter.OwnerId.Value || a.OwnerTeamId == filter.OwnerId.Value
            );
        if (filter.TenantId.HasValue)
        {
            if (!_tenantContext.HasAccessToTenant(filter.TenantId.Value))
                return Forbid();
            query = query.Where(a => a.TenantId == filter.TenantId.Value);
        }
        if (!string.IsNullOrEmpty(filter.Search))
            query = query.Where(a =>
                a.Name.Contains(filter.Search)
                || (
                    a.DeviceComputerDnsName != null
                    && a.DeviceComputerDnsName.Contains(filter.Search)
                )
                || a.ExternalId.Contains(filter.Search)
            );

        var totalCount = await query.CountAsync(ct);

        var assetIds = await query
            .OrderBy(a => a.Name)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(a => a.Id)
            .ToListAsync(ct);

        var recurringCounts = await _dbContext
            .VulnerabilityAssetEpisodes.AsNoTracking()
            .Where(episode => assetIds.Contains(episode.AssetId))
            .GroupBy(episode => new { episode.AssetId, episode.VulnerabilityId })
            .Select(group => new { group.Key.AssetId, IsRecurring = group.Count() > 1 })
            .Where(item => item.IsRecurring)
            .GroupBy(item => item.AssetId)
            .Select(group => new { AssetId = group.Key, Count = group.Count() })
            .ToListAsync(ct);

        var recurringCountsByAssetId = recurringCounts.ToDictionary(
            item => item.AssetId,
            item => item.Count
        );

        var itemRows = await query
            .OrderBy(a => a.Name)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(a => new
            {
                a.Id,
                a.ExternalId,
                Name = a.AssetType == AssetType.Device ? a.DeviceComputerDnsName ?? a.Name : a.Name,
                AssetType = a.AssetType.ToString(),
                Criticality = a.Criticality.ToString(),
                OwnerType = a.OwnerType.ToString(),
                a.OwnerUserId,
                a.OwnerTeamId,
                SecurityProfileName = _dbContext
                    .AssetSecurityProfiles.Where(profile => profile.Id == a.SecurityProfileId)
                    .Select(profile => profile.Name)
                    .FirstOrDefault(),
                VulnerabilityCount = _dbContext.VulnerabilityAssets.Count(va => va.AssetId == a.Id),
            })
            .ToListAsync(ct);

        var items = itemRows
            .Select(a => new AssetDto(
                a.Id,
                a.ExternalId,
                a.Name,
                a.AssetType,
                a.Criticality,
                a.OwnerType,
                a.OwnerUserId,
                a.OwnerTeamId,
                a.SecurityProfileName,
                a.VulnerabilityCount,
                recurringCountsByAssetId.TryGetValue(a.Id, out var recurringCount)
                    ? recurringCount
                    : 0
            ))
            .ToList();

        return Ok(
            new PagedResponse<AssetDto>(
                items,
                totalCount,
                pagination.Page,
                pagination.BoundedPageSize
            )
        );
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<AssetDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var asset = await _dbContext.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null)
            return NotFound();

        var securityProfile = asset.SecurityProfileId is Guid securityProfileId
            ? await _dbContext
                .AssetSecurityProfiles.AsNoTracking()
                .Where(profile => profile.Id == securityProfileId)
                .Select(profile => new AssetSecurityProfileSummaryDto(
                    profile.Id,
                    profile.Name,
                    profile.EnvironmentClass.ToString(),
                    profile.InternetReachability.ToString(),
                    profile.ConfidentialityRequirement.ToString(),
                    profile.IntegrityRequirement.ToString(),
                    profile.AvailabilityRequirement.ToString()
                ))
                .FirstOrDefaultAsync(ct)
            : null;

        var episodeRows = await _dbContext
            .VulnerabilityAssetEpisodes.AsNoTracking()
            .Where(episode => episode.AssetId == id)
            .OrderBy(episode => episode.VulnerabilityId)
            .ThenBy(episode => episode.EpisodeNumber)
            .Select(episode => new
            {
                episode.VulnerabilityId,
                episode.EpisodeNumber,
                episode.Status,
                episode.FirstSeenAt,
                episode.LastSeenAt,
                episode.ResolvedAt,
            })
            .ToListAsync(ct);

        var episodesByVulnerabilityId = episodeRows
            .GroupBy(row => row.VulnerabilityId)
            .ToDictionary(
                group => group.Key,
                group =>
                    group
                        .Select(row => new AssetVulnerabilityEpisodeDto(
                            row.EpisodeNumber,
                            row.Status.ToString(),
                            row.FirstSeenAt,
                            row.LastSeenAt,
                            row.ResolvedAt
                        ))
                        .ToList() as IReadOnlyList<AssetVulnerabilityEpisodeDto>
            );

        var softwareEpisodeRows = await _dbContext
            .DeviceSoftwareInstallationEpisodes.AsNoTracking()
            .Where(episode => episode.DeviceAssetId == id)
            .OrderBy(episode => episode.SoftwareAssetId)
            .ThenBy(episode => episode.EpisodeNumber)
            .Select(episode => new SoftwareCorrelationRow(
                episode.SoftwareAssetId,
                episode.EpisodeNumber,
                episode.FirstSeenAt,
                episode.LastSeenAt,
                episode.RemovedAt
            ))
            .ToListAsync(ct);

        var softwareEpisodesByAssetId = softwareEpisodeRows
            .GroupBy(row => row.SoftwareAssetId)
            .ToDictionary(
                group => group.Key,
                group =>
                    group
                        .Select(row => new AssetSoftwareInstallationEpisodeDto(
                            row.EpisodeNumber,
                            row.FirstSeenAt,
                            row.LastSeenAt,
                            row.RemovedAt
                        ))
                        .ToList() as IReadOnlyList<AssetSoftwareInstallationEpisodeDto>
            );

        var softwareRows = await _dbContext
            .DeviceSoftwareInstallations.AsNoTracking()
            .Where(link => link.DeviceAssetId == id)
            .Join(
                _dbContext.Assets,
                link => link.SoftwareAssetId,
                software => software.Id,
                (link, software) =>
                    new
                    {
                        software.Id,
                        software.Name,
                        software.ExternalId,
                        link.LastSeenAt,
                    }
            )
            .ToListAsync(ct);

        var cpeBindingAssetIds = softwareRows
            .Select(row => row.Id)
            .Append(asset.Id)
            .Distinct()
            .ToList();
        var cpeBindingsBySoftwareAssetId = await _dbContext
            .SoftwareCpeBindings.AsNoTracking()
            .Where(binding => cpeBindingAssetIds.Contains(binding.SoftwareAssetId))
            .ToDictionaryAsync(
                binding => binding.SoftwareAssetId,
                binding => new SoftwareCpeBindingDto(
                    binding.Id,
                    binding.Cpe23Uri,
                    binding.BindingMethod.ToString(),
                    binding.Confidence.ToString(),
                    binding.MatchedVendor,
                    binding.MatchedProduct,
                    binding.MatchedVersion,
                    binding.LastValidatedAt
                ),
                ct
            );

        var softwareNamesByAssetId = softwareRows.ToDictionary(row => row.Id, row => row.Name);

        var assessmentsByVulnerabilityId = await _dbContext
            .VulnerabilityAssetAssessments.AsNoTracking()
            .Where(assessment => assessment.AssetId == id)
            .ToDictionaryAsync(assessment => assessment.VulnerabilityId, ct);

        var possibleCorrelationsByVulnerabilityId = episodeRows
            .GroupBy(row => row.VulnerabilityId)
            .ToDictionary(
                group => group.Key,
                group =>
                    RankPossibleCorrelatedSoftware(
                        softwareEpisodeRows,
                        softwareNamesByAssetId,
                        group
                            .Select(row => new AssetVulnerabilityEpisodeDto(
                                row.EpisodeNumber,
                                row.Status.ToString(),
                                row.FirstSeenAt,
                                row.LastSeenAt,
                                row.ResolvedAt
                            ))
                            .ToList()
                    )
            );

        var vulnerabilityRows = await _dbContext
            .VulnerabilityAssets.AsNoTracking()
            .Where(va => va.AssetId == id)
            .Join(
                _dbContext.Vulnerabilities,
                va => va.VulnerabilityId,
                v => v.Id,
                (va, v) =>
                    new
                    {
                        v.Id,
                        v.ExternalId,
                        v.Title,
                        v.Description,
                        VendorSeverity = v.VendorSeverity.ToString(),
                        v.CvssVector,
                        v.PublishedDate,
                        Status = va.Status.ToString(),
                        va.DetectedDate,
                        va.ResolvedDate,
                    }
            )
            .ToListAsync(ct);

        var softwareVulnerabilityRows =
            asset.AssetType == AssetType.Software
                ? await _dbContext
                    .SoftwareVulnerabilityMatches.AsNoTracking()
                    .Where(match => match.SoftwareAssetId == id && match.ResolvedAt == null)
                    .Join(
                        _dbContext.Vulnerabilities.AsNoTracking(),
                        match => match.VulnerabilityId,
                        vulnerability => vulnerability.Id,
                        (match, vulnerability) =>
                            new AssetKnownSoftwareVulnerabilityDto(
                                vulnerability.Id,
                                vulnerability.ExternalId,
                                vulnerability.Title,
                                vulnerability.VendorSeverity.ToString(),
                                vulnerability.CvssScore,
                                vulnerability.CvssVector,
                                match.MatchMethod.ToString(),
                                match.Confidence.ToString(),
                                match.Evidence,
                                match.FirstSeenAt,
                                match.LastSeenAt,
                                match.ResolvedAt
                            )
                    )
                    .OrderBy(item => item.ExternalId)
                    .ToListAsync(ct)
                : [];

        var vulnerabilities = vulnerabilityRows
            .Select(row =>
            {
                assessmentsByVulnerabilityId.TryGetValue(row.Id, out var assessment);
                episodesByVulnerabilityId.TryGetValue(row.Id, out var episodeHistory);
                possibleCorrelationsByVulnerabilityId.TryGetValue(
                    row.Id,
                    out var correlatedSoftware
                );

                return new AssetVulnerabilityDto(
                    row.Id,
                    row.ExternalId,
                    row.Title,
                    row.Description,
                    row.VendorSeverity,
                    assessment?.BaseScore,
                    assessment?.BaseVector ?? row.CvssVector,
                    row.PublishedDate,
                    assessment?.EffectiveSeverity.ToString() ?? row.VendorSeverity,
                    assessment?.EffectiveScore,
                    assessment?.ReasonSummary,
                    row.Status,
                    row.DetectedDate,
                    row.ResolvedDate,
                    episodeHistory?.Count ?? 0,
                    episodeHistory ?? [],
                    correlatedSoftware ?? []
                );
            })
            .ToList();

        var softwareInventory = softwareRows
            .Select(row => new AssetSoftwareInstallationDto(
                row.Id,
                row.Name,
                row.ExternalId,
                row.LastSeenAt,
                cpeBindingsBySoftwareAssetId.TryGetValue(row.Id, out var cpeBinding)
                    ? cpeBinding
                    : null,
                softwareEpisodesByAssetId.TryGetValue(row.Id, out var episodes)
                    ? episodes.Count
                    : 0,
                softwareEpisodesByAssetId.TryGetValue(row.Id, out var episodeHistory)
                    ? episodeHistory
                    : []
            ))
            .ToList();

        return Ok(
            new AssetDetailDto(
                asset.Id,
                asset.ExternalId,
                asset.Name,
                asset.Description,
                asset.AssetType.ToString(),
                asset.Criticality.ToString(),
                asset.OwnerType.ToString(),
                asset.OwnerUserId,
                asset.OwnerTeamId,
                asset.FallbackTeamId,
                securityProfile,
                asset.DeviceComputerDnsName,
                asset.DeviceHealthStatus,
                asset.DeviceOsPlatform,
                asset.DeviceOsVersion,
                asset.DeviceRiskScore,
                asset.DeviceLastSeenAt,
                asset.DeviceLastIpAddress,
                asset.DeviceAadDeviceId,
                cpeBindingsBySoftwareAssetId.TryGetValue(asset.Id, out var assetCpeBinding)
                    ? assetCpeBinding
                    : null,
                asset.Metadata,
                vulnerabilities,
                softwareInventory,
                softwareVulnerabilityRows
            )
        );
    }

    [HttpPut("{id:guid}/owner")]
    [Authorize(Policy = Policies.ModifyVulnerabilities)]
    public async Task<IActionResult> AssignOwner(
        Guid id,
        [FromBody] AssignOwnerRequest request,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<OwnerType>(request.OwnerType, out var ownerType))
            return BadRequest(new ProblemDetails { Title = "Invalid owner type" });

        var result = ownerType switch
        {
            OwnerType.User => await _assetService.AssignOwnerAsync(id, request.OwnerId, ct),
            OwnerType.Team => await _assetService.AssignTeamOwnerAsync(id, request.OwnerId, ct),
            _ => throw new ArgumentOutOfRangeException(),
        };

        if (!result.IsSuccess)
            return NotFound(new ProblemDetails { Title = result.Error });

        return NoContent();
    }

    [HttpPut("{id:guid}/security-profile")]
    [Authorize(Policy = Policies.ModifyVulnerabilities)]
    public async Task<IActionResult> AssignSecurityProfile(
        Guid id,
        [FromBody] AssignAssetSecurityProfileRequest request,
        CancellationToken ct
    )
    {
        if (request.SecurityProfileId.HasValue)
        {
            var exists = await _dbContext
                .AssetSecurityProfiles.AsNoTracking()
                .AnyAsync(profile => profile.Id == request.SecurityProfileId.Value, ct);
            if (!exists)
            {
                return BadRequest(new ProblemDetails { Title = "Security profile not found" });
            }
        }

        var result = await _assetService.AssignSecurityProfileAsync(
            id,
            request.SecurityProfileId,
            ct
        );
        if (!result.IsSuccess)
            return NotFound(new ProblemDetails { Title = result.Error });

        await _assessmentService.RecalculateForAssetAsync(id, ct);
        await _dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPut("{id:guid}/criticality")]
    [Authorize(Policy = Policies.ModifyVulnerabilities)]
    public async Task<IActionResult> SetCriticality(
        Guid id,
        [FromBody] SetCriticalityRequest request,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<Criticality>(request.Criticality, out var criticality))
            return BadRequest(new ProblemDetails { Title = "Invalid criticality value" });

        var result = await _assetService.SetCriticalityAsync(id, criticality, ct);
        if (!result.IsSuccess)
            return NotFound(new ProblemDetails { Title = result.Error });

        return NoContent();
    }

    [HttpPut("{id:guid}/software-cpe-binding")]
    [Authorize(Policy = Policies.ModifyVulnerabilities)]
    public async Task<IActionResult> AssignSoftwareCpeBinding(
        Guid id,
        [FromBody] AssignSoftwareCpeBindingRequest request,
        CancellationToken ct
    )
    {
        var asset = await _dbContext.Assets.FirstOrDefaultAsync(current => current.Id == id, ct);
        if (asset is null)
        {
            return NotFound(new ProblemDetails { Title = "Asset not found" });
        }

        if (asset.AssetType != AssetType.Software)
        {
            return BadRequest(
                new ProblemDetails { Title = "Only software assets support CPE bindings" }
            );
        }

        var existingBinding = await _dbContext.SoftwareCpeBindings.FirstOrDefaultAsync(
            binding => binding.SoftwareAssetId == id,
            ct
        );

        if (string.IsNullOrWhiteSpace(request.Cpe23Uri))
        {
            if (existingBinding is not null)
            {
                _dbContext.SoftwareCpeBindings.Remove(existingBinding);
                await _dbContext.SaveChangesAsync(ct);
            }

            return NoContent();
        }

        if (!TryParseCpe23(request.Cpe23Uri, out var cpe))
        {
            return BadRequest(new ProblemDetails { Title = "Invalid CPE 2.3 URI" });
        }

        var now = DateTimeOffset.UtcNow;
        if (existingBinding is null)
        {
            await _dbContext.SoftwareCpeBindings.AddAsync(
                SoftwareCpeBinding.Create(
                    asset.TenantId,
                    asset.Id,
                    request.Cpe23Uri.Trim(),
                    CpeBindingMethod.Manual,
                    MatchConfidence.High,
                    cpe.Vendor,
                    cpe.Product,
                    cpe.Version,
                    now
                ),
                ct
            );
        }
        else
        {
            existingBinding.Update(
                request.Cpe23Uri.Trim(),
                CpeBindingMethod.Manual,
                MatchConfidence.High,
                cpe.Vendor,
                cpe.Product,
                cpe.Version,
                now
            );
        }

        await _dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("bulk-assign")]
    [Authorize(Policy = Policies.ModifyVulnerabilities)]
    public async Task<ActionResult<BulkAssignResponse>> BulkAssign(
        [FromBody] BulkAssignRequest request,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<OwnerType>(request.OwnerType, out var ownerType))
            return BadRequest(new ProblemDetails { Title = "Invalid owner type" });

        var result = await _assetService.BulkAssignOwnerAsync(
            request.AssetIds,
            request.OwnerId,
            ownerType,
            ct
        );
        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        return Ok(new BulkAssignResponse(result.Value));
    }

    private static IReadOnlyList<string> RankPossibleCorrelatedSoftware(
        IReadOnlyList<SoftwareCorrelationRow> softwareRows,
        IReadOnlyDictionary<Guid, string> softwareNamesByAssetId,
        IReadOnlyList<AssetVulnerabilityEpisodeDto> vulnerabilityEpisodes
    )
    {
        return softwareRows
            .Select(softwareRow =>
            {
                if (!softwareNamesByAssetId.TryGetValue(softwareRow.SoftwareAssetId, out var name))
                {
                    return null;
                }

                var matchingEpisode = vulnerabilityEpisodes
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
                            Name = name,
                            Score = score,
                            Age = age,
                        };
                    })
                    .OrderByDescending(item => item.Score)
                    .ThenBy(item => item.Age)
                    .FirstOrDefault();

                return matchingEpisode;
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

    private static bool TryParseCpe23(string? cpe23Uri, out ParsedCpeComponents components)
    {
        components = default!;
        if (string.IsNullOrWhiteSpace(cpe23Uri))
        {
            return false;
        }

        var parts = cpe23Uri.Split(':');
        if (parts.Length < 6 || !string.Equals(parts[0], "cpe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        components = new ParsedCpeComponents(
            NormalizeToken(parts[3]),
            NormalizeToken(parts[4]),
            NormalizeVersion(parts[5])
        );

        return !string.IsNullOrWhiteSpace(components.Vendor)
            && !string.IsNullOrWhiteSpace(components.Product);
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        return new string(chars);
    }

    private static string? NormalizeVersion(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || value is "*" or "-"
            ? null
            : value.Trim().ToLowerInvariant();
    }
}
