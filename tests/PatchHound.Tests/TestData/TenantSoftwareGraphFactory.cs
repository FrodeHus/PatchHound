using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Tests.TestData;

// Phase-2: VulnerabilityDefinition, TenantVulnerability, SoftwareVulnerabilityMatch,
// NormalizedSoftwareVulnerabilityProjection, and VulnerabilityAsset deleted.
// Graph record retains only TenantSoftware (the only property accessed by callers).
internal static class TenantSoftwareGraphFactory
{
    internal sealed record Graph(TenantSoftware TenantSoftware);

    public static async Task<Graph> SeedAsync(PatchHoundDbContext dbContext, Guid tenantId)
    {
        var normalizedSoftware = NormalizedSoftware.Create(
            "agent",
            "contoso",
            "cpe:contoso:agent",
            "cpe:2.3:a:contoso:agent:*:*:*:*:*:*:*:*",
            SoftwareNormalizationMethod.ExplicitCpe,
            SoftwareNormalizationConfidence.High,
            new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero)
        );
        var tenantSoftware = TenantSoftware.Create(
            tenantId,
            null,
            normalizedSoftware.Id,
            new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero)
        );

        var deviceOne = Asset.Create(tenantId, "device-1", AssetType.Device, "Device 1", Criticality.High);
        deviceOne.UpdateDeviceDetails("Device 1", null, null, null, null, null, null, null);
        var deviceTwo = Asset.Create(tenantId, "device-2", AssetType.Device, "Device 2", Criticality.Medium);
        deviceTwo.UpdateDeviceDetails("Device 2", null, null, null, null, null, null, null);
        var softwareOne = Asset.Create(tenantId, "software-1", AssetType.Software, "Contoso Agent", Criticality.Low);
        var softwareTwo = Asset.Create(tenantId, "software-2", AssetType.Software, "Contoso Agent", Criticality.Low);
        var profile = AssetSecurityProfile.Create(
            tenantId,
            "Server Profile",
            null,
            EnvironmentClass.Server,
            InternetReachability.InternalNetwork,
            SecurityRequirementLevel.High,
            SecurityRequirementLevel.High,
            SecurityRequirementLevel.High
        );
        deviceOne.AssignSecurityProfile(profile.Id);

        await dbContext.AddRangeAsync(
            normalizedSoftware,
            tenantSoftware,
            deviceOne,
            deviceTwo,
            softwareOne,
            softwareTwo,
            profile
        );
        await dbContext.NormalizedSoftwareAliases.AddRangeAsync(
            NormalizedSoftwareAlias.Create(
                normalizedSoftware.Id,
                SoftwareIdentitySourceSystem.Defender,
                "software-1",
                "Contoso Agent",
                "Contoso",
                "1.0",
                SoftwareNormalizationConfidence.High,
                "Resolved via software CPE binding.",
                DateTimeOffset.UtcNow
            ),
            NormalizedSoftwareAlias.Create(
                normalizedSoftware.Id,
                SoftwareIdentitySourceSystem.Defender,
                "software-2",
                "Contoso Agent",
                "Contoso",
                "2.0",
                SoftwareNormalizationConfidence.High,
                "Resolved via software CPE binding.",
                DateTimeOffset.UtcNow
            )
        );
        await dbContext.NormalizedSoftwareInstallations.AddRangeAsync(
            NormalizedSoftwareInstallation.Create(
                tenantId,
                null,
                tenantSoftware.Id,
                softwareOne.Id,
                deviceOne.Id,
                SoftwareIdentitySourceSystem.Defender,
                "1.0",
                new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero),
                null,
                true,
                1
            ),
            NormalizedSoftwareInstallation.Create(
                tenantId,
                null,
                tenantSoftware.Id,
                softwareTwo.Id,
                deviceTwo.Id,
                SoftwareIdentitySourceSystem.Defender,
                "2.0",
                new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 9, 0, 0, 0, TimeSpan.Zero),
                null,
                true,
                1
            )
        );
        await dbContext.DeviceSoftwareInstallations.AddRangeAsync(
            DeviceSoftwareInstallation.Create(
                tenantId,
                deviceOne.Id,
                softwareOne.Id,
                new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero)
            ),
            DeviceSoftwareInstallation.Create(
                tenantId,
                deviceTwo.Id,
                softwareTwo.Id,
                new DateTimeOffset(2026, 3, 9, 0, 0, 0, TimeSpan.Zero)
            )
        );
        await dbContext.SaveChangesAsync();

        return new Graph(tenantSoftware);
    }
}
