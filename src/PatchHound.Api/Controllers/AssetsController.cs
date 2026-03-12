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
using PatchHound.Infrastructure.Tenants;

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
    private readonly NormalizedSoftwareProjectionService _normalizedSoftwareProjectionService;
    private readonly ITenantContext _tenantContext;

    public AssetsController(
        PatchHoundDbContext dbContext,
        AssetService assetService,
        VulnerabilityAssessmentService assessmentService,
        NormalizedSoftwareProjectionService normalizedSoftwareProjectionService,
        ITenantContext tenantContext
    )
    {
        _dbContext = dbContext;
        _assetService = assetService;
        _assessmentService = assessmentService;
        _normalizedSoftwareProjectionService = normalizedSoftwareProjectionService;
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
        if (_tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        var activeSnapshotId = await ResolveActiveVulnerabilitySnapshotIdAsync(currentTenantId, ct);

        var query = _dbContext
            .Assets.AsNoTracking()
            .Where(a => a.TenantId == currentTenantId)
            .AsQueryable();

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
            .GroupBy(episode => new { episode.AssetId, episode.TenantVulnerabilityId })
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
                VulnerabilityCount = _dbContext.VulnerabilityAssets.Count(va =>
                    va.AssetId == a.Id && va.SnapshotId == activeSnapshotId
                ),
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
        if (_tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        var activeSnapshotId = await ResolveActiveVulnerabilitySnapshotIdAsync(currentTenantId, ct);

        var asset = await _dbContext
            .Assets.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == currentTenantId, ct);
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
            .OrderBy(episode => episode.TenantVulnerabilityId)
            .ThenBy(episode => episode.EpisodeNumber)
            .Select(episode => new
            {
                VulnerabilityId = episode.TenantVulnerabilityId,
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

        var relevantSoftwareAssets = softwareRows
            .Select(row => new { row.Id, row.ExternalId })
            .ToList();
        if (asset.AssetType == AssetType.Software)
        {
            relevantSoftwareAssets.Add(new { Id = asset.Id, asset.ExternalId });
        }

        var relevantSoftwareExternalIds = relevantSoftwareAssets
            .Select(item => item.ExternalId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var tenantSoftwareIdsByExternalId = relevantSoftwareExternalIds.Count == 0
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
                    item.TenantId == asset.TenantId
                    && item.SourceSystem == SoftwareIdentitySourceSystem.Defender
                    && relevantSoftwareExternalIds.Contains(item.ExternalSoftwareId)
                )
                .GroupBy(item => item.ExternalSoftwareId)
                .Select(group => new { ExternalSoftwareId = group.Key, TenantSoftwareId = group.Select(item => item.Id).First() })
                .ToDictionaryAsync(
                    item => item.ExternalSoftwareId,
                    item => (Guid?)item.TenantSoftwareId,
                    ct
                );
        var normalizedSoftwareIdsByExternalId = relevantSoftwareExternalIds.Count == 0
            ? new Dictionary<string, Guid>(StringComparer.Ordinal)
            : await _dbContext
                .TenantSoftware.AsNoTracking()
                .Join(
                    _dbContext.NormalizedSoftwareAliases.AsNoTracking(),
                    tenantSoftware => tenantSoftware.NormalizedSoftwareId,
                    alias => alias.NormalizedSoftwareId,
                    (tenantSoftware, alias) => new
                    {
                        tenantSoftware.NormalizedSoftwareId,
                        tenantSoftware.TenantId,
                        alias.SourceSystem,
                        alias.ExternalSoftwareId,
                    }
                )
                .Where(item =>
                    item.TenantId == asset.TenantId
                    && item.SourceSystem == SoftwareIdentitySourceSystem.Defender
                    && relevantSoftwareExternalIds.Contains(item.ExternalSoftwareId)
                )
                .GroupBy(item => item.ExternalSoftwareId)
                .Select(group => new { ExternalSoftwareId = group.Key, NormalizedSoftwareId = group.Select(item => item.NormalizedSoftwareId).First() })
                .ToDictionaryAsync(item => item.ExternalSoftwareId, item => item.NormalizedSoftwareId, ct);

        var normalizedSoftwareIds = normalizedSoftwareIdsByExternalId.Values.Distinct().ToList();
        var cpeBindingsByNormalizedSoftwareId = await _dbContext
            .SoftwareCpeBindings.AsNoTracking()
            .Where(binding => normalizedSoftwareIds.Contains(binding.NormalizedSoftwareId))
            .ToDictionaryAsync(
                binding => binding.NormalizedSoftwareId,
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
            .Where(assessment =>
                assessment.AssetId == id && assessment.SnapshotId == activeSnapshotId
            )
            .ToDictionaryAsync(assessment => assessment.TenantVulnerabilityId, ct);

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
            .Where(va => va.AssetId == id && va.SnapshotId == activeSnapshotId)
            .Join(
                _dbContext.TenantVulnerabilities.AsNoTracking(),
                va => va.TenantVulnerabilityId,
                tv => tv.Id,
                (va, tv) =>
                    new
                    {
                        Id = tv.Id,
                        ExternalId = tv.VulnerabilityDefinition.ExternalId,
                        Title = tv.VulnerabilityDefinition.Title,
                        Description = tv.VulnerabilityDefinition.Description,
                        VendorSeverity = tv.VulnerabilityDefinition.VendorSeverity.ToString(),
                        CvssVector = tv.VulnerabilityDefinition.CvssVector,
                        PublishedDate = tv.VulnerabilityDefinition.PublishedDate,
                        Status = va.Status.ToString(),
                        va.DetectedDate,
                        va.ResolvedDate,
                    }
            )
            .ToListAsync(ct);

        IReadOnlyList<AssetKnownSoftwareVulnerabilityDto> softwareVulnerabilityRows = [];
        if (asset.AssetType == AssetType.Software)
        {
            var softwareVulnerabilityItems = await _dbContext
                .SoftwareVulnerabilityMatches.AsNoTracking()
                .Where(match => match.SoftwareAssetId == id && match.ResolvedAt == null)
                .Join(
                    _dbContext.VulnerabilityDefinitions.AsNoTracking(),
                    match => match.VulnerabilityDefinitionId,
                    vulnerability => vulnerability.Id,
                    (match, vulnerability) =>
                        new
                        {
                            vulnerability.Id,
                            vulnerability.ExternalId,
                            vulnerability.Title,
                            VendorSeverity = vulnerability.VendorSeverity.ToString(),
                            vulnerability.CvssScore,
                            vulnerability.CvssVector,
                            MatchMethod = match.MatchMethod.ToString(),
                            Confidence = match.Confidence.ToString(),
                            match.Evidence,
                            match.FirstSeenAt,
                            match.LastSeenAt,
                            match.ResolvedAt,
                        }
                )
                .OrderBy(item => item.ExternalId)
                .ToListAsync(ct);

            softwareVulnerabilityRows = softwareVulnerabilityItems
                .Select(item => new AssetKnownSoftwareVulnerabilityDto(
                    item.Id,
                    item.ExternalId,
                    item.Title,
                    item.VendorSeverity,
                    item.CvssScore,
                    item.CvssVector,
                    item.MatchMethod,
                    item.Confidence,
                    item.Evidence,
                    item.FirstSeenAt,
                    item.LastSeenAt,
                    item.ResolvedAt
                ))
                .ToList();
        }

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
                tenantSoftwareIdsByExternalId.TryGetValue(row.ExternalId, out var tenantSoftwareId)
                    ? tenantSoftwareId
                    : null,
                row.Name,
                row.ExternalId,
                row.LastSeenAt,
                normalizedSoftwareIdsByExternalId.TryGetValue(row.ExternalId, out var normalizedSoftwareId)
                    ? cpeBindingsByNormalizedSoftwareId.GetValueOrDefault(normalizedSoftwareId)
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
                asset.AssetType == AssetType.Software
                    && tenantSoftwareIdsByExternalId.TryGetValue(asset.ExternalId, out var assetTenantSoftwareId)
                    ? assetTenantSoftwareId
                    : null,
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
                asset.AssetType == AssetType.Software
                    && normalizedSoftwareIdsByExternalId.TryGetValue(asset.ExternalId, out var assetNormalizedSoftwareId)
                    ? cpeBindingsByNormalizedSoftwareId.GetValueOrDefault(assetNormalizedSoftwareId)
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

        await _normalizedSoftwareProjectionService.SyncTenantAsync(asset.TenantId, ct);

        var tenantSoftware = await _dbContext
            .TenantSoftware.AsNoTracking()
            .Join(
                _dbContext.NormalizedSoftwareAliases.AsNoTracking(),
                current => current.NormalizedSoftwareId,
                alias => alias.NormalizedSoftwareId,
                (current, alias) => new
                {
                    current.NormalizedSoftwareId,
                    current.TenantId,
                    alias.SourceSystem,
                    alias.ExternalSoftwareId,
                }
            )
            .Where(item =>
                item.TenantId == asset.TenantId
                && item.SourceSystem == SoftwareIdentitySourceSystem.Defender
                && item.ExternalSoftwareId == asset.ExternalId
            )
            .Select(item => item.NormalizedSoftwareId)
            .FirstOrDefaultAsync(ct);
        if (tenantSoftware == Guid.Empty)
        {
            return BadRequest(
                new ProblemDetails { Title = "Software asset is not linked to a normalized software definition" }
            );
        }

        var existingBinding = await _dbContext.SoftwareCpeBindings.FirstOrDefaultAsync(
            binding => binding.NormalizedSoftwareId == tenantSoftware,
            ct
        );
        var normalizedSoftware = await _dbContext.NormalizedSoftware.FirstAsync(
            item => item.Id == tenantSoftware,
            ct
        );

        if (string.IsNullOrWhiteSpace(request.Cpe23Uri))
        {
            if (existingBinding is not null)
            {
                _dbContext.SoftwareCpeBindings.Remove(existingBinding);
                normalizedSoftware.UpdateIdentity(
                    normalizedSoftware.CanonicalName,
                    normalizedSoftware.CanonicalVendor,
                    BuildCanonicalProductKey(
                        normalizedSoftware.CanonicalVendor,
                        normalizedSoftware.CanonicalName,
                        null
                    ),
                    null,
                    SoftwareNormalizationMethod.Heuristic,
                    normalizedSoftware.Confidence,
                    DateTimeOffset.UtcNow
                );
                await _dbContext.SaveChangesAsync(ct);
                await _normalizedSoftwareProjectionService.SyncTenantAsync(asset.TenantId, ct);
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
                    tenantSoftware,
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

        normalizedSoftware.UpdateIdentity(
            cpe.Product,
            cpe.Vendor,
            BuildCanonicalProductKey(cpe.Vendor, cpe.Product, request.Cpe23Uri.Trim()),
            request.Cpe23Uri.Trim(),
            SoftwareNormalizationMethod.ExplicitCpe,
            SoftwareNormalizationConfidence.High,
            now
        );

        await _dbContext.SaveChangesAsync(ct);
        await _normalizedSoftwareProjectionService.SyncTenantAsync(asset.TenantId, ct);
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

    private static string BuildCanonicalProductKey(
        string? vendor,
        string product,
        string? cpe23Uri
    )
    {
        if (!string.IsNullOrWhiteSpace(cpe23Uri) && TryParseCpe23(cpe23Uri, out var cpe))
        {
            return $"cpe:{NormalizeToken(cpe.Vendor)}:{NormalizeToken(cpe.Product)}";
        }

        return $"{NormalizeToken(vendor)}|{NormalizeToken(product)}";
    }
}
