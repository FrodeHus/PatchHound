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
using PatchHound.Api.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/assets")]
[Authorize]
public class AssetsController : ControllerBase
{
    private sealed record ParsedCpeComponents(string Vendor, string Product, string? Version);

    private readonly PatchHoundDbContext _dbContext;
    private readonly AssetService _assetService;
    private readonly VulnerabilityAssessmentService _assessmentService;
    private readonly NormalizedSoftwareProjectionService _normalizedSoftwareProjectionService;
    private readonly ITenantContext _tenantContext;
    private readonly TenantSnapshotResolver _snapshotResolver;
    private readonly AssetDetailQueryService _detailQueryService;
    private readonly RiskRefreshService _riskRefreshService;
    private readonly IDeviceRuleEvaluationService _deviceRuleEvaluationService;

    public AssetsController(
        PatchHoundDbContext dbContext,
        AssetService assetService,
        VulnerabilityAssessmentService assessmentService,
        NormalizedSoftwareProjectionService normalizedSoftwareProjectionService,
        ITenantContext tenantContext,
        TenantSnapshotResolver snapshotResolver,
        AssetDetailQueryService detailQueryService,
        RiskRefreshService riskRefreshService,
        IDeviceRuleEvaluationService deviceRuleEvaluationService
    )
    {
        _dbContext = dbContext;
        _assetService = assetService;
        _assessmentService = assessmentService;
        _normalizedSoftwareProjectionService = normalizedSoftwareProjectionService;
        _tenantContext = tenantContext;
        _snapshotResolver = snapshotResolver;
        _detailQueryService = detailQueryService;
        _riskRefreshService = riskRefreshService;
        _deviceRuleEvaluationService = deviceRuleEvaluationService;
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
        var activeSnapshotId = await _snapshotResolver.ResolveActiveVulnerabilitySnapshotIdAsync(currentTenantId, ct);

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
        if (!string.IsNullOrEmpty(filter.DeviceGroup))
            query = query.Where(a =>
                (a.DeviceGroupName != null && a.DeviceGroupName.Contains(filter.DeviceGroup))
                || (a.DeviceGroupId != null && a.DeviceGroupId.Contains(filter.DeviceGroup))
            );
        if (!string.IsNullOrEmpty(filter.HealthStatus))
            query = query.Where(a => a.DeviceHealthStatus == filter.HealthStatus);
        if (!string.IsNullOrEmpty(filter.RiskScore))
            query = query.Where(a => a.DeviceRiskScore == filter.RiskScore);
        if (!string.IsNullOrEmpty(filter.ExposureLevel))
            query = query.Where(a => a.DeviceExposureLevel == filter.ExposureLevel);
        if (!string.IsNullOrEmpty(filter.Tag))
            query = query.Where(a =>
                _dbContext.AssetTags.Any(t => t.AssetId == a.Id && t.Tag.Contains(filter.Tag))
            );
        if (filter.BusinessLabelId.HasValue)
            query = query.Where(a =>
                _dbContext.AssetBusinessLabels.Any(link =>
                    link.AssetId == a.Id && link.BusinessLabelId == filter.BusinessLabelId.Value)
            );
        if (!string.IsNullOrEmpty(filter.OnboardingStatus))
            query = query.Where(a => a.DeviceOnboardingStatus == filter.OnboardingStatus);

        var totalCount = await query.CountAsync(ct);

        // Pre-compute vulnerability counts per asset so we can sort without
        // a correlated COUNT subquery in the ORDER BY clause.
        var rankedQuery = query
            .Select(a => new
            {
                Asset = a,
                CurrentRiskScore = _dbContext.AssetRiskScores
                    .Where(score => score.AssetId == a.Id)
                    .Select(score => (decimal?)score.OverallScore)
                    .FirstOrDefault(),
                VulnerabilityCount = _dbContext.VulnerabilityAssets.Count(va =>
                    va.AssetId == a.Id && va.SnapshotId == activeSnapshotId
                ),
            });

        var assetIds = await rankedQuery
            .OrderByDescending(item => item.CurrentRiskScore ?? 0m)
            .ThenByDescending(item => item.VulnerabilityCount)
            .ThenBy(item => item.Asset.Name)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(item => item.Asset.Id)
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

        // Fetch full DTOs using the pre-computed ID list instead of
        // re-running the sort+pagination query with correlated aggregates.
        var itemRows = await _dbContext.Assets.AsNoTracking()
            .Where(a => assetIds.Contains(a.Id))
            .Select(a => new
            {
                a.Id,
                a.ExternalId,
                Name = a.AssetType == AssetType.Device
                    ? a.DeviceComputerDnsName ?? a.Name
                    : a.Name,
                AssetType = a.AssetType.ToString(),
                CurrentRiskScore = _dbContext.AssetRiskScores
                    .Where(score => score.AssetId == a.Id)
                    .Select(score => (decimal?)score.OverallScore)
                    .FirstOrDefault(),
                a.DeviceGroupName,
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
                a.DeviceHealthStatus,
                a.DeviceRiskScore,
                a.DeviceExposureLevel,
                a.DeviceOnboardingStatus,
                a.DeviceValue,
            })
            .ToListAsync(ct);

        // Preserve the sort order from the paginated ID query
        var itemRowsById = itemRows.ToDictionary(a => a.Id);
        itemRows = assetIds.Where(id => itemRowsById.ContainsKey(id))
            .Select(id => itemRowsById[id]).ToList();

        var assetTagsByAssetId = await _dbContext.AssetTags
            .AsNoTracking()
            .Where(t => assetIds.Contains(t.AssetId))
            .GroupBy(t => t.AssetId)
            .Select(g => new { AssetId = g.Key, Tags = g.Select(t => t.Tag).ToArray() })
            .ToDictionaryAsync(g => g.AssetId, g => g.Tags, ct);
        var businessLabelsByAssetId = await _dbContext.AssetBusinessLabels
            .AsNoTracking()
            .Where(link => assetIds.Contains(link.AssetId))
            .Select(link => new
            {
                link.AssetId,
                link.BusinessLabel.Id,
                link.BusinessLabel.Name,
                link.BusinessLabel.Description,
                link.BusinessLabel.Color,
            })
            .ToListAsync(ct);
        var businessLabelLookup = businessLabelsByAssetId
            .GroupBy(item => item.AssetId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(item => item.Id)
                    .Select(itemGroup => itemGroup.First())
                    .OrderBy(item => item.Name)
                    .Select(item => new BusinessLabelSummaryDto(
                        item.Id,
                        item.Name,
                        item.Description,
                        item.Color
                    ))
                    .ToList() as IReadOnlyList<BusinessLabelSummaryDto>
            );

        var items = itemRows
            .Select(a => new AssetDto(
                a.Id,
                a.ExternalId,
                a.Name,
                a.AssetType,
                a.CurrentRiskScore,
                a.DeviceGroupName,
                a.Criticality,
                a.OwnerType,
                a.OwnerUserId,
                a.OwnerTeamId,
                a.SecurityProfileName,
                a.VulnerabilityCount,
                recurringCountsByAssetId.TryGetValue(a.Id, out var recurringCount)
                    ? recurringCount
                    : 0,
                a.DeviceHealthStatus,
                a.DeviceRiskScore,
                a.DeviceExposureLevel,
                assetTagsByAssetId.TryGetValue(a.Id, out var tags) ? tags : Array.Empty<string>(),
                businessLabelLookup.TryGetValue(a.Id, out var businessLabels) ? businessLabels : [],
                a.DeviceOnboardingStatus,
                a.DeviceValue
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

        var detail = await _detailQueryService.BuildAsync(currentTenantId, id, ct);
        if (detail is null)
            return NotFound();

        return Ok(detail);
    }

    [HttpPut("{id:guid}/business-labels")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> AssignBusinessLabels(
        Guid id,
        [FromBody] UpdateAssetBusinessLabelsRequest request,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var asset = await _dbContext.Assets
            .FirstOrDefaultAsync(item => item.Id == id && item.TenantId == currentTenantId, ct);
        if (asset is null)
            return NotFound(new ProblemDetails { Title = "Asset not found." });

        var labelIds = (request.BusinessLabelIds ?? [])
            .Distinct()
            .ToList();

        var validLabelIds = labelIds.Count == 0
            ? []
            : await _dbContext.BusinessLabels
                .Where(item => item.TenantId == currentTenantId && item.IsActive && labelIds.Contains(item.Id))
                .Select(item => item.Id)
                .ToListAsync(ct);

        if (validLabelIds.Count != labelIds.Count)
            return BadRequest(new ProblemDetails { Title = "One or more business labels are invalid for this tenant." });

        var existingLinks = await _dbContext.AssetBusinessLabels
            .Where(item =>
                item.AssetId == id
                && item.SourceType == AssetBusinessLabel.ManualSourceType
                && item.SourceKey == AssetBusinessLabel.ManualSourceKey)
            .ToListAsync(ct);
        var existingLabelIds = existingLinks.Select(item => item.BusinessLabelId).ToHashSet();

        _dbContext.AssetBusinessLabels.RemoveRange(
            existingLinks.Where(item => !validLabelIds.Contains(item.BusinessLabelId))
        );

        var userId = _tenantContext.CurrentUserId;
        foreach (var labelId in validLabelIds.Where(labelId => !existingLabelIds.Contains(labelId)))
        {
            await _dbContext.AssetBusinessLabels.AddAsync(
                AssetBusinessLabel.CreateManual(id, labelId, userId),
                ct
            );
        }

        await _dbContext.SaveChangesAsync(ct);
        return NoContent();
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

        var asset = await _dbContext.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null)
            return NotFound();
        if (!_tenantContext.HasAccessToTenant(asset.TenantId))
            return Forbid();

        var result = ownerType switch
        {
            OwnerType.User => await _assetService.AssignOwnerAsync(id, request.OwnerId, ct),
            OwnerType.Team => await _assetService.AssignTeamOwnerAsync(id, request.OwnerId, ct),
            _ => throw new ArgumentOutOfRangeException(),
        };

        if (!result.IsSuccess)
            return NotFound(new ProblemDetails { Title = result.Error });

        await _riskRefreshService.RefreshForAssetAsync(
            asset.TenantId,
            id,
            recalculateAssessments: false,
            ct
        );

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
        var asset = await _dbContext.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null)
            return NotFound();
        if (!_tenantContext.HasAccessToTenant(asset.TenantId))
            return Forbid();

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

        await _riskRefreshService.RefreshForAssetAsync(
            asset.TenantId,
            id,
            recalculateAssessments: true,
            ct
        );

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

        var asset = await _dbContext.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null)
            return NotFound();
        if (!_tenantContext.HasAccessToTenant(asset.TenantId))
            return Forbid();

        var result = await _assetService.SetCriticalityAsync(id, criticality, ct);
        if (!result.IsSuccess)
            return NotFound(new ProblemDetails { Title = result.Error });

        await _riskRefreshService.RefreshForAssetAsync(
            asset.TenantId,
            id,
            recalculateAssessments: false,
            ct
        );

        return NoContent();
    }

    [HttpPost("{id:guid}/criticality/reset")]
    [Authorize(Policy = Policies.ModifyVulnerabilities)]
    public async Task<IActionResult> ResetCriticalityOverride(
        Guid id,
        CancellationToken ct
    )
    {
        var asset = await _dbContext.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null)
            return NotFound();
        if (!_tenantContext.HasAccessToTenant(asset.TenantId))
            return Forbid();

        var result = await _assetService.ClearManualCriticalityOverrideAsync(id, ct);
        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        // AssetsController is deleted in Task 13 of the canonical cleanup. Until then,
        // pass the asset id as the device id — the new service looks up a Device with
        // that id; if the id doesn't correspond to a canonical Device row, the call is
        // a silent no-op, which is acceptable for the interim.
        await _deviceRuleEvaluationService.EvaluateCriticalityForDeviceAsync(asset.TenantId, id, ct);
        await _riskRefreshService.RefreshForAssetAsync(
            asset.TenantId,
            id,
            recalculateAssessments: false,
            ct
        );

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

        if (!_tenantContext.HasAccessToTenant(asset.TenantId))
            return Forbid();

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
                    normalizedSoftware.Category,
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
            normalizedSoftware.Category,
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

        var assetTenantIds = await _dbContext
            .Assets.AsNoTracking()
            .Where(a => request.AssetIds.Contains(a.Id))
            .Select(a => a.TenantId)
            .Distinct()
            .ToListAsync(ct);
        if (assetTenantIds.Any(tid => !_tenantContext.HasAccessToTenant(tid)))
            return Forbid();

        var result = await _assetService.BulkAssignOwnerAsync(
            request.AssetIds,
            request.OwnerId,
            ownerType,
            ct
        );
        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        var assetTenantMap = await _dbContext.Assets.AsNoTracking()
            .Where(a => request.AssetIds.Contains(a.Id))
            .Select(a => new { a.Id, a.TenantId })
            .ToListAsync(ct);
        foreach (var tenantAssets in assetTenantMap.GroupBy(item => item.TenantId))
        {
            await _riskRefreshService.RefreshForAssetsAsync(
                tenantAssets.Key,
                tenantAssets.Select(item => item.Id).ToList(),
                recalculateAssessments: false,
                ct
            );
        }

        return Ok(new BulkAssignResponse(result.Value));
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
