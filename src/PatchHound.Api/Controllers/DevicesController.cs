using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Assets;
using PatchHound.Api.Models.Devices;
using PatchHound.Api.Models.Software;
using PatchHound.Api.Services;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services.RiskScoring;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Services.Inventory;

namespace PatchHound.Api.Controllers;

// Phase 1 canonical cleanup (Task 13): Device-native API surface.
// Parallels the legacy AssetsController's device endpoints but reads and
// writes the canonical Device table directly. AssetsController remains
// alive until Phase 5 rewires vulnerability/software tables off the Asset
// navigation and deletes the legacy inventory surface entirely.
[ApiController]
[Route("api/devices")]
[Authorize]
public class DevicesController : ControllerBase
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly DeviceService _deviceService;
    private readonly ITenantContext _tenantContext;
    private readonly TenantSnapshotResolver _snapshotResolver;
    private readonly DeviceDetailQueryService _detailQueryService;
    private readonly RiskRefreshService _riskRefreshService;
    private readonly IDeviceRuleEvaluationService _deviceRuleEvaluationService;

    public DevicesController(
        PatchHoundDbContext dbContext,
        DeviceService deviceService,
        ITenantContext tenantContext,
        TenantSnapshotResolver snapshotResolver,
        DeviceDetailQueryService detailQueryService,
        RiskRefreshService riskRefreshService,
        IDeviceRuleEvaluationService deviceRuleEvaluationService
    )
    {
        _dbContext = dbContext;
        _deviceService = deviceService;
        _tenantContext = tenantContext;
        _snapshotResolver = snapshotResolver;
        _detailQueryService = detailQueryService;
        _riskRefreshService = riskRefreshService;
        _deviceRuleEvaluationService = deviceRuleEvaluationService;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<DeviceDto>>> List(
        [FromQuery] DeviceFilterQuery filter,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var activeSnapshotId = await _snapshotResolver.ResolveActiveVulnerabilitySnapshotIdAsync(
            currentTenantId,
            ct
        );

        var query = _dbContext.Devices
            .AsNoTracking()
            .Where(d => d.TenantId == currentTenantId)
            .AsQueryable();

        if (
            !string.IsNullOrEmpty(filter.Criticality)
            && Enum.TryParse<Criticality>(filter.Criticality, out var criticality)
        )
            query = query.Where(d => d.Criticality == criticality);
        if (
            !string.IsNullOrEmpty(filter.OwnerType)
            && Enum.TryParse<OwnerType>(filter.OwnerType, out var ownerType)
        )
            query = query.Where(d => d.OwnerType == ownerType);
        if (filter.UnassignedOnly == true)
            query = query.Where(d => d.OwnerUserId == null && d.OwnerTeamId == null);
        if (filter.OwnerId.HasValue)
            query = query.Where(d =>
                d.OwnerUserId == filter.OwnerId.Value || d.OwnerTeamId == filter.OwnerId.Value
            );
        if (filter.TenantId.HasValue)
        {
            if (!_tenantContext.HasAccessToTenant(filter.TenantId.Value))
                return Forbid();
            query = query.Where(d => d.TenantId == filter.TenantId.Value);
        }
        if (!string.IsNullOrEmpty(filter.Search))
            query = query.Where(d =>
                d.Name.Contains(filter.Search)
                || (d.ComputerDnsName != null && d.ComputerDnsName.Contains(filter.Search))
                || d.ExternalId.Contains(filter.Search)
            );
        if (!string.IsNullOrEmpty(filter.DeviceGroup))
            query = query.Where(d =>
                (d.GroupName != null && d.GroupName.Contains(filter.DeviceGroup))
                || (d.GroupId != null && d.GroupId.Contains(filter.DeviceGroup))
            );
        if (!string.IsNullOrEmpty(filter.HealthStatus))
            query = query.Where(d => d.HealthStatus == filter.HealthStatus);
        if (!string.IsNullOrEmpty(filter.Tag))
            query = query.Where(d =>
                _dbContext.DeviceTags.Any(t => t.DeviceId == d.Id && t.Value.Contains(filter.Tag))
            );
        if (filter.BusinessLabelId.HasValue)
            query = query.Where(d =>
                _dbContext.DeviceBusinessLabels.Any(link =>
                    link.DeviceId == d.Id && link.BusinessLabelId == filter.BusinessLabelId.Value
                )
            );
        if (!string.IsNullOrEmpty(filter.OnboardingStatus))
            query = query.Where(d => d.OnboardingStatus == filter.OnboardingStatus);

        var rankedQuery = query.Select(d => new
        {
            Device = d,
            CurrentRiskScore = _dbContext.DeviceRiskScores
                .Where(score => score.DeviceId == d.Id)
                .Select(score => (decimal?)score.OverallScore)
                .FirstOrDefault(),
            VulnerabilityCount = _dbContext.DeviceVulnerabilityExposures
                .Where(e => e.TenantId == _tenantContext.CurrentTenantId.Value && e.DeviceId == d.Id)
                .Select(e => e.VulnerabilityId)
                .Distinct()
                .Count(),
        });

        if (!string.IsNullOrWhiteSpace(filter.RiskBand))
        {
            rankedQuery = filter.RiskBand.Trim().ToLowerInvariant() switch
            {
                "none" => rankedQuery.Where(item => item.CurrentRiskScore == null || item.CurrentRiskScore <= 0m),
                "low" => rankedQuery.Where(item =>
                    item.CurrentRiskScore > 0m && item.CurrentRiskScore < RiskBand.MediumThreshold),
                "medium" => rankedQuery.Where(item =>
                    item.CurrentRiskScore >= RiskBand.MediumThreshold && item.CurrentRiskScore < RiskBand.HighThreshold),
                "high" => rankedQuery.Where(item =>
                    item.CurrentRiskScore >= RiskBand.HighThreshold && item.CurrentRiskScore < RiskBand.CriticalThreshold),
                "critical" => rankedQuery.Where(item => item.CurrentRiskScore >= RiskBand.CriticalThreshold),
                _ => rankedQuery,
            };
        }

        var totalCount = await rankedQuery.CountAsync(ct);

        var deviceIds = await rankedQuery
            .OrderByDescending(item => item.CurrentRiskScore ?? 0m)
            .ThenByDescending(item => item.VulnerabilityCount)
            .ThenBy(item => item.Device.Name)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(item => item.Device.Id)
            .ToListAsync(ct);

        var recurringCountsByDeviceId = await _dbContext.ExposureEpisodes
            .AsNoTracking()
            .Where(episode => deviceIds.Contains(episode.Exposure.DeviceId))
            .GroupBy(episode => episode.Exposure.DeviceId)
            .Select(group => new { DeviceId = group.Key, Count = group.Count(episode => episode.EpisodeNumber > 1) })
            .ToDictionaryAsync(item => item.DeviceId, item => item.Count, ct);

        var itemRows = await _dbContext.Devices
            .AsNoTracking()
            .Where(d => deviceIds.Contains(d.Id))
            .Select(d => new
            {
                d.Id,
                d.ExternalId,
                Name = d.ComputerDnsName ?? d.Name,
                CurrentRiskScore = _dbContext.DeviceRiskScores
                    .Where(score => score.DeviceId == d.Id)
                    .Select(score => (decimal?)score.OverallScore)
                    .FirstOrDefault(),
                d.GroupName,
                Criticality = d.Criticality.ToString(),
                OwnerType = d.OwnerType.ToString(),
                d.OwnerUserId,
                d.OwnerTeamId,
                SecurityProfileName = _dbContext.SecurityProfiles
                    .Where(profile => profile.Id == d.SecurityProfileId)
                    .Select(profile => profile.Name)
                    .FirstOrDefault(),
                VulnerabilityCount = _dbContext.DeviceVulnerabilityExposures
                    .Where(e => e.TenantId == _tenantContext.CurrentTenantId.Value && e.DeviceId == d.Id)
                .Select(e => e.VulnerabilityId)
                .Distinct()
                .Count(),
                d.HealthStatus,
                d.OnboardingStatus,
                d.DeviceValue,
            })
            .ToListAsync(ct);

        var itemRowsById = itemRows.ToDictionary(d => d.Id);
        itemRows = deviceIds
            .Where(id => itemRowsById.ContainsKey(id))
            .Select(id => itemRowsById[id])
            .ToList();

        var tagsByDeviceId = await _dbContext.DeviceTags
            .AsNoTracking()
            .Where(t => deviceIds.Contains(t.DeviceId))
            .GroupBy(t => t.DeviceId)
            .Select(g => new { DeviceId = g.Key, Tags = g.Select(t => t.Value).ToArray() })
            .ToDictionaryAsync(g => g.DeviceId, g => g.Tags, ct);

        var businessLabelsByDeviceId = await _dbContext.DeviceBusinessLabels
            .AsNoTracking()
            .Where(link => link.TenantId == _tenantContext.CurrentTenantId.Value && deviceIds.Contains(link.DeviceId))
            .Select(link => new
            {
                link.DeviceId,
                link.BusinessLabel.Id,
                link.BusinessLabel.Name,
                link.BusinessLabel.Description,
                link.BusinessLabel.Color,
                link.BusinessLabel.WeightCategory,
            })
            .ToListAsync(ct);
        var businessLabelLookup = businessLabelsByDeviceId
            .GroupBy(item => item.DeviceId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(item => item.Id)
                    .Select(inner => inner.First())
                    .OrderBy(item => item.Name)
                    .Select(item => new BusinessLabelSummaryDto(
                        item.Id,
                        item.Name,
                        item.Description,
                        item.Color,
                        item.WeightCategory.ToString(),
                        BusinessLabel.CategoryWeights[item.WeightCategory]
                    ))
                    .ToList() as IReadOnlyList<BusinessLabelSummaryDto>
            );

        var items = itemRows
            .Select(d => new DeviceDto(
                d.Id,
                d.ExternalId,
                d.Name,
                d.CurrentRiskScore,
                d.CurrentRiskScore.HasValue ? RiskBand.FromScore(d.CurrentRiskScore.Value) : "None",
                d.GroupName,
                d.Criticality,
                d.OwnerType,
                d.OwnerUserId,
                d.OwnerTeamId,
                d.SecurityProfileName,
                d.VulnerabilityCount,
                recurringCountsByDeviceId.TryGetValue(d.Id, out var recurring) ? recurring : 0,
                d.HealthStatus,
                tagsByDeviceId.TryGetValue(d.Id, out var tags) ? tags : Array.Empty<string>(),
                businessLabelLookup.TryGetValue(d.Id, out var labels) ? labels : [],
                d.OnboardingStatus,
                d.DeviceValue
            ))
            .ToList();

        return Ok(
            new PagedResponse<DeviceDto>(
                items,
                totalCount,
                pagination.Page,
                pagination.BoundedPageSize
            )
        );
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<DeviceDetailDto>> Get(Guid id, CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var detail = await _detailQueryService.BuildAsync(currentTenantId, id, ct);
        if (detail is null)
            return NotFound();

        return Ok(detail);
    }

    [HttpGet("{deviceId:guid}/exposures")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<DeviceExposureDto>>> ListExposures(
        Guid deviceId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct)
    {
        if (_tenantContext.CurrentTenantId is null)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var device = await _dbContext.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is null)
            return NotFound();

        var query = _dbContext.DeviceVulnerabilityExposures
            .AsNoTracking()
            .Where(e => e.DeviceId == deviceId);
        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(e => e.Status == ExposureStatus.Open)
            .ThenByDescending(e => e.LastObservedAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .Select(e => new DeviceExposureDto(
                e.Id,
                e.VulnerabilityId,
                e.Vulnerability.ExternalId,
                e.Vulnerability.Title,
                e.Vulnerability.VendorSeverity.ToString(),
                e.MatchedVersion,
                e.MatchSource.ToString(),
                e.Status.ToString(),
                e.FirstObservedAt,
                e.LastObservedAt,
                e.ResolvedAt,
                _dbContext.ExposureAssessments
                    .Where(a => a.DeviceVulnerabilityExposureId == e.Id)
                    .Select(a => (decimal?)a.EnvironmentalCvss)
                    .FirstOrDefault()))
            .ToListAsync(ct);

        return Ok(new PagedResponse<DeviceExposureDto>(items, total, pagination.Page, pagination.BoundedPageSize));
    }

    [HttpPut("{id:guid}/business-labels")]
    [Authorize(Policy = Policies.ConfigureTenant)]
    public async Task<IActionResult> AssignBusinessLabels(
        Guid id,
        [FromBody] UpdateDeviceBusinessLabelsRequest request,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var device = await _dbContext.Devices.FirstOrDefaultAsync(
            d => d.Id == id && d.TenantId == currentTenantId,
            ct
        );
        if (device is null)
            return NotFound(new ProblemDetails { Title = "Device not found." });

        var labelIds = (request.BusinessLabelIds ?? []).Distinct().ToList();

        var validLabelIds = labelIds.Count == 0
            ? []
            : await _dbContext.BusinessLabels
                .Where(item =>
                    item.TenantId == currentTenantId
                    && item.IsActive
                    && labelIds.Contains(item.Id)
                )
                .Select(item => item.Id)
                .ToListAsync(ct);

        if (validLabelIds.Count != labelIds.Count)
            return BadRequest(
                new ProblemDetails
                {
                    Title = "One or more business labels are invalid for this tenant.",
                }
            );

        // Only touch manual links; rule-assigned links are reconciled by
        // DeviceRuleEvaluationService and must not be deleted here.
        var existingManualLinks = await _dbContext.DeviceBusinessLabels
            .Where(item =>
                item.DeviceId == id
                && item.SourceType == DeviceBusinessLabel.ManualSourceType)
            .ToListAsync(ct);
        var existingManualLabelIds = existingManualLinks.Select(item => item.BusinessLabelId).ToHashSet();

        _dbContext.DeviceBusinessLabels.RemoveRange(
            existingManualLinks.Where(item => !validLabelIds.Contains(item.BusinessLabelId))
        );

        var assignedBy = _tenantContext.CurrentUserId;
        foreach (var labelId in validLabelIds.Where(labelId => !existingManualLabelIds.Contains(labelId)))
        {
            await _dbContext.DeviceBusinessLabels.AddAsync(
                DeviceBusinessLabel.CreateManual(currentTenantId, id, labelId, assignedBy),
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
        [FromBody] AssignDeviceOwnerRequest request,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<OwnerType>(request.OwnerType, out var ownerType))
            return BadRequest(new ProblemDetails { Title = "Invalid owner type" });

        var device = await _dbContext.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null)
            return NotFound();
        if (!_tenantContext.HasAccessToTenant(device.TenantId))
            return Forbid();

        var result = ownerType switch
        {
            OwnerType.User => await _deviceService.AssignOwnerAsync(id, request.OwnerId, ct),
            OwnerType.Team => await _deviceService.AssignTeamOwnerAsync(id, request.OwnerId, ct),
            _ => throw new ArgumentOutOfRangeException(),
        };

        if (!result.IsSuccess)
            return NotFound(new ProblemDetails { Title = result.Error });

        await _riskRefreshService.RefreshForDeviceAsync(
            device.TenantId,
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
        [FromBody] AssignDeviceSecurityProfileRequest request,
        CancellationToken ct
    )
    {
        var device = await _dbContext.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null)
            return NotFound();
        if (!_tenantContext.HasAccessToTenant(device.TenantId))
            return Forbid();

        if (request.SecurityProfileId.HasValue)
        {
            var exists = await _dbContext.SecurityProfiles
                .AsNoTracking()
                .AnyAsync(profile => profile.Id == request.SecurityProfileId.Value, ct);
            if (!exists)
                return BadRequest(new ProblemDetails { Title = "Security profile not found" });
        }

        var result = await _deviceService.AssignSecurityProfileAsync(
            id,
            request.SecurityProfileId,
            ct
        );
        if (!result.IsSuccess)
            return NotFound(new ProblemDetails { Title = result.Error });

        await _riskRefreshService.RefreshForDeviceAsync(
            device.TenantId,
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
        [FromBody] SetDeviceCriticalityRequest request,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<Criticality>(request.Criticality, out var criticality))
            return BadRequest(new ProblemDetails { Title = "Invalid criticality value" });

        var device = await _dbContext.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null)
            return NotFound();
        if (!_tenantContext.HasAccessToTenant(device.TenantId))
            return Forbid();

        var result = await _deviceService.SetCriticalityAsync(id, criticality, ct);
        if (!result.IsSuccess)
            return NotFound(new ProblemDetails { Title = result.Error });

        await _riskRefreshService.RefreshForDeviceAsync(
            device.TenantId,
            id,
            recalculateAssessments: false,
            ct
        );

        return NoContent();
    }

    [HttpPost("{id:guid}/criticality/reset")]
    [Authorize(Policy = Policies.ModifyVulnerabilities)]
    public async Task<IActionResult> ResetCriticalityOverride(Guid id, CancellationToken ct)
    {
        var device = await _dbContext.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null)
            return NotFound();
        if (!_tenantContext.HasAccessToTenant(device.TenantId))
            return Forbid();

        var result = await _deviceService.ClearManualCriticalityOverrideAsync(id, ct);
        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        await _deviceRuleEvaluationService.EvaluateCriticalityForDeviceAsync(device.TenantId, id, ct);
        await _riskRefreshService.RefreshForDeviceAsync(
            device.TenantId,
            id,
            recalculateAssessments: false,
            ct
        );

        return NoContent();
    }

    [HttpPost("bulk-assign")]
    [Authorize(Policy = Policies.ModifyVulnerabilities)]
    public async Task<ActionResult<BulkAssignDevicesResponse>> BulkAssign(
        [FromBody] BulkAssignDevicesRequest request,
        CancellationToken ct
    )
    {
        if (!Enum.TryParse<OwnerType>(request.OwnerType, out var ownerType))
            return BadRequest(new ProblemDetails { Title = "Invalid owner type" });

        var deviceTenantIds = await _dbContext.Devices
            .AsNoTracking()
            .Where(d => request.DeviceIds.Contains(d.Id))
            .Select(d => d.TenantId)
            .Distinct()
            .ToListAsync(ct);
        if (deviceTenantIds.Any(tid => !_tenantContext.HasAccessToTenant(tid)))
            return Forbid();

        var result = await _deviceService.BulkAssignOwnerAsync(
            request.DeviceIds,
            request.OwnerId,
            ownerType,
            ct
        );
        if (!result.IsSuccess)
            return BadRequest(new ProblemDetails { Title = result.Error });

        var deviceTenantMap = await _dbContext.Devices
            .AsNoTracking()
            .Where(d => request.DeviceIds.Contains(d.Id))
            .Select(d => new { d.Id, d.TenantId })
            .ToListAsync(ct);
        foreach (var tenantDevices in deviceTenantMap.GroupBy(item => item.TenantId))
        {
            await _riskRefreshService.RefreshForDevicesAsync(
                tenantDevices.Key,
                tenantDevices.Select(item => item.Id).ToList(),
                recalculateAssessments: false,
                ct
            );
        }

        return Ok(new BulkAssignDevicesResponse(result.Value));
    }

    [HttpGet("{id:guid}/software")]
    [Authorize(Policy = Policies.ViewVulnerabilities)]
    public async Task<ActionResult<PagedResponse<DeviceSoftwareItemDto>>> GetSoftware(
        Guid id,
        [FromQuery] PaginationQuery pagination,
        CancellationToken ct
    )
    {
        if (_tenantContext.CurrentTenantId is not Guid currentTenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var deviceExists = await _dbContext.Devices.AsNoTracking()
            .AnyAsync(d => d.Id == id && d.TenantId == currentTenantId, ct);
        if (!deviceExists)
            return NotFound();

        // Group InstalledSoftware by SoftwareProductId to get one row per product.
        var grouped = await _dbContext.InstalledSoftware.AsNoTracking()
            .Where(i => i.TenantId == currentTenantId && i.DeviceId == id)
            .GroupBy(i => i.SoftwareProductId)
            .Select(g => new
            {
                SoftwareProductId = g.Key,
                LastSeenAt = g.Max(i => i.LastSeenAt),
            })
            .ToListAsync(ct);

        var totalCount = grouped.Count;

        var page = grouped
            .OrderByDescending(g => g.LastSeenAt)
            .Skip(pagination.Skip)
            .Take(pagination.BoundedPageSize)
            .ToList();

        var productIds = page.Select(g => g.SoftwareProductId).ToList();

        var productNames = await _dbContext.SoftwareProducts.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(ct);

        var nameById = productNames.ToDictionary(p => p.Id, p => p.Name);

        var tenantSoftwareRows = await _dbContext.SoftwareTenantRecords.AsNoTracking()
            .Where(item => item.TenantId == currentTenantId && productIds.Contains(item.SoftwareProductId))
            .Select(item => new { item.SoftwareProductId, item.Id })
            .ToListAsync(ct);

        var tenantSoftwareIdByProductId = tenantSoftwareRows
            .GroupBy(item => item.SoftwareProductId)
            .ToDictionary(group => group.Key, group => group.First().Id);

        var openVulnCounts = await _dbContext.DeviceVulnerabilityExposures.AsNoTracking()
            .Where(e =>
                e.TenantId == currentTenantId
                && e.DeviceId == id
                && e.Status == ExposureStatus.Open
                && e.SoftwareProductId != null
                && productIds.Contains(e.SoftwareProductId.Value))
            .GroupBy(e => e.SoftwareProductId!.Value)
            .Select(g => new { SoftwareProductId = g.Key, Count = g.Select(e => e.VulnerabilityId).Distinct().Count() })
            .ToListAsync(ct);

        var vulnCountById = openVulnCounts.ToDictionary(v => v.SoftwareProductId, v => v.Count);

        var items = page.Select(g => new DeviceSoftwareItemDto(
            TenantSoftwareId: tenantSoftwareIdByProductId.TryGetValue(g.SoftwareProductId, out var tenantSoftwareId)
                ? tenantSoftwareId
                : null,
            SoftwareProductId: g.SoftwareProductId,
            SoftwareName: nameById.GetValueOrDefault(g.SoftwareProductId, string.Empty),
            LastSeenAt: g.LastSeenAt,
            OpenVulnerabilityCount: vulnCountById.GetValueOrDefault(g.SoftwareProductId, 0)
        )).ToList();

        return Ok(new PagedResponse<DeviceSoftwareItemDto>(items, totalCount, pagination.Page, pagination.BoundedPageSize));
    }
}
