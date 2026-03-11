using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Tests.TestData;

internal static class TenantSoftwareGraphFactory
{
    internal sealed record Graph(
        TenantSoftware TenantSoftware,
        VulnerabilityDefinition VulnerabilityDefinition,
        TenantVulnerability TenantVulnerability
    );

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

        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-1000",
            "Contoso Agent vulnerability",
            "Description",
            Severity.Critical,
            "NVD",
            9.8m,
            null,
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)
        );
        var tenantVulnerability = TenantVulnerability.Create(
            tenantId,
            definition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow
        );

        await dbContext.AddRangeAsync(
            normalizedSoftware,
            tenantSoftware,
            deviceOne,
            deviceTwo,
            softwareOne,
            softwareTwo,
            profile,
            definition,
            tenantVulnerability
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
        await dbContext.SoftwareVulnerabilityMatches.AddRangeAsync(
            SoftwareVulnerabilityMatch.Create(
                tenantId,
                softwareOne.Id,
                definition.Id,
                SoftwareVulnerabilityMatchMethod.CpeBinding,
                MatchConfidence.High,
                "match-one",
                new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero)
            ),
            SoftwareVulnerabilityMatch.Create(
                tenantId,
                softwareTwo.Id,
                definition.Id,
                SoftwareVulnerabilityMatchMethod.CpeBinding,
                MatchConfidence.High,
                "match-two",
                new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero)
            )
        );
        await dbContext.NormalizedSoftwareVulnerabilityProjections.AddAsync(
            NormalizedSoftwareVulnerabilityProjection.Create(
                tenantId,
                tenantSoftware.Id,
                definition.Id,
                SoftwareVulnerabilityMatchMethod.CpeBinding,
                MatchConfidence.High,
                2,
                2,
                2,
                new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero),
                null,
                """
                [{"method":"CpeBinding","confidence":"High","evidence":"contoso-agent","firstSeenAt":"2026-03-10T00:00:00+00:00","lastSeenAt":"2026-03-10T00:00:00+00:00","resolvedAt":null}]
                """
            )
        );
        await dbContext.VulnerabilityAssets.AddRangeAsync(
            VulnerabilityAsset.Create(
                tenantVulnerability.Id,
                deviceOne.Id,
                new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero)
            ),
            VulnerabilityAsset.Create(
                tenantVulnerability.Id,
                deviceTwo.Id,
                new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero)
            )
        );
        await dbContext.SaveChangesAsync();

        return new Graph(tenantSoftware, definition, tenantVulnerability);
    }
}
