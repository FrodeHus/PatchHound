using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Models.Assets;
using PatchHound.Api.Models.Devices;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Services;

// Phase 1 canonical cleanup (Task 13): Device-anchored detail query that
// reads from canonical device tables (Device, DeviceRiskScore,
// DeviceBusinessLabels, DeviceTags) plus the Asset-keyed remediation summary
// via RemediationTaskQueryService.BuildDeviceSummaryAsync (still takes a
// device id and is query-filter compatible today). Vulnerability/software
// inventory sections are intentionally NOT included here — those tables are
// still AssetId-keyed and Phase 5 rewires them off the Asset navigation.
// Until that rewire lands, callers who need full vulnerability/software
// context for a device use the legacy /api/assets/{id} endpoint.
public class DeviceDetailQueryService
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly RemediationTaskQueryService _remediationTaskQueryService;

    public DeviceDetailQueryService(
        PatchHoundDbContext dbContext,
        RemediationTaskQueryService remediationTaskQueryService
    )
    {
        _dbContext = dbContext;
        _remediationTaskQueryService = remediationTaskQueryService;
    }

    public async Task<DeviceDetailDto?> BuildAsync(
        Guid tenantId,
        Guid deviceId,
        CancellationToken ct
    )
    {
        var device = await _dbContext.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.TenantId == tenantId, ct);
        if (device is null)
            return null;

        var securityProfile = device.SecurityProfileId is Guid securityProfileId
            ? await _dbContext.SecurityProfiles
                .AsNoTracking()
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

        var businessLabels = await _dbContext.DeviceBusinessLabels
            .AsNoTracking()
            .Where(link => link.DeviceId == deviceId)
            .OrderBy(link => link.BusinessLabel.Name)
            .Select(link => new BusinessLabelSummaryDto(
                link.BusinessLabel.Id,
                link.BusinessLabel.Name,
                link.BusinessLabel.Description,
                link.BusinessLabel.Color
            ))
            .ToListAsync(ct);
        businessLabels = businessLabels
            .GroupBy(label => label.Id)
            .Select(group => group.First())
            .OrderBy(label => label.Name)
            .ToList();

        var ownerUserName = device.OwnerUserId is Guid ownerUserId
            ? await _dbContext.Users
                .AsNoTracking()
                .Where(user => user.Id == ownerUserId)
                .Select(user => user.DisplayName)
                .FirstOrDefaultAsync(ct)
            : null;

        var relevantTeamIds = new[] { device.OwnerTeamId, device.FallbackTeamId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        var teamNamesById = relevantTeamIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Teams
                .AsNoTracking()
                .Where(team => relevantTeamIds.Contains(team.Id))
                .ToDictionaryAsync(team => team.Id, team => team.Name, ct);

        var tags = await _dbContext.DeviceTags
            .AsNoTracking()
            .Where(tag => tag.DeviceId == deviceId)
            .Select(tag => tag.Value)
            .ToArrayAsync(ct);

        var deviceRiskScore = await _dbContext.DeviceRiskScores
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.DeviceId == deviceId)
            .Select(item => new
            {
                item.OverallScore,
                item.MaxEpisodeRiskScore,
                item.CriticalCount,
                item.HighCount,
                item.MediumCount,
                item.LowCount,
                item.OpenEpisodeCount,
                item.CalculatedAt,
            })
            .FirstOrDefaultAsync(ct);

        DeviceRiskDetailDto? risk = deviceRiskScore is null
            ? null
            : new DeviceRiskDetailDto(
                deviceRiskScore.OverallScore,
                deviceRiskScore.MaxEpisodeRiskScore,
                ResolveRiskBand(deviceRiskScore.OverallScore),
                deviceRiskScore.OpenEpisodeCount,
                deviceRiskScore.CriticalCount,
                deviceRiskScore.HighCount,
                deviceRiskScore.MediumCount,
                deviceRiskScore.LowCount,
                deviceRiskScore.CalculatedAt
            );

        var remediation = await _remediationTaskQueryService.BuildDeviceSummaryAsync(
            tenantId,
            deviceId,
            ct
        );

        return new DeviceDetailDto(
            device.Id,
            device.ExternalId,
            device.Name,
            device.Description,
            device.Criticality.ToString(),
            device.CriticalitySource is { Length: > 0 }
                ? new DeviceCriticalityDetailDto(
                    device.CriticalitySource,
                    device.CriticalityReason,
                    device.CriticalityRuleId,
                    device.CriticalityUpdatedAt
                )
                : null,
            device.OwnerType.ToString(),
            ownerUserName,
            device.OwnerUserId,
            device.OwnerTeamId is Guid ownerTeamId
                ? teamNamesById.GetValueOrDefault(ownerTeamId)
                : null,
            device.OwnerTeamId,
            device.FallbackTeamId is Guid fallbackTeamId
                ? teamNamesById.GetValueOrDefault(fallbackTeamId)
                : null,
            device.FallbackTeamId,
            securityProfile,
            device.ComputerDnsName,
            device.HealthStatus,
            device.OsPlatform,
            device.OsVersion,
            device.ExternalRiskLabel,
            device.LastSeenAt,
            device.LastIpAddress,
            device.AadDeviceId,
            device.GroupId,
            device.GroupName,
            device.ExposureLevel,
            device.IsAadJoined,
            device.OnboardingStatus,
            device.DeviceValue,
            businessLabels,
            risk,
            remediation,
            tags,
            device.Metadata
        );
    }

    private static string ResolveRiskBand(decimal score)
    {
        if (score >= 900m)
            return "Critical";
        if (score >= 750m)
            return "High";
        if (score >= 500m)
            return "Medium";
        return "Low";
    }
}
