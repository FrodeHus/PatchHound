using FluentAssertions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Tests.Infrastructure;

public class EnvironmentalSeverityCalculatorTests
{
    private readonly EnvironmentalSeverityCalculator _calculator = new();

    [Fact]
    public void Calculate_LocalOnlyProfile_DowngradesNetworkReachability()
    {
        var vulnerability = VulnerabilityDefinition.Create(
            "CVE-2026-4000",
            "Remote issue",
            "Desc",
            Severity.Critical,
            "MicrosoftDefender",
            9.8m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H"
        );
        var asset = Asset.Create(
            Guid.NewGuid(),
            "device-1",
            AssetType.Device,
            "Device",
            Criticality.High
        );
        var profile = AssetSecurityProfile.Create(
            asset.TenantId,
            "Isolated device",
            null,
            EnvironmentClass.OT,
            InternetReachability.LocalOnly,
            SecurityRequirementLevel.Medium,
            SecurityRequirementLevel.Medium,
            SecurityRequirementLevel.Medium
        );

        var result = _calculator.Calculate(vulnerability, asset, profile);

        result.EffectiveScore.Should().BeLessThan(vulnerability.CvssScore!.Value);
        result.EffectiveSeverity.Should().NotBe(Severity.Critical);
        result.ReasonSummary.Should().Contain("Modified attack vector changed");
    }

    [Fact]
    public void Calculate_HighRequirements_CanIncreaseEffectiveScore()
    {
        var vulnerability = VulnerabilityDefinition.Create(
            "CVE-2026-4001",
            "Sensitive data issue",
            "Desc",
            Severity.Medium,
            "MicrosoftDefender",
            5.5m,
            "CVSS:3.1/AV:L/AC:L/PR:L/UI:N/S:U/C:L/I:L/A:L"
        );
        var asset = Asset.Create(
            Guid.NewGuid(),
            "device-2",
            AssetType.Device,
            "Device",
            Criticality.High
        );
        var profile = AssetSecurityProfile.Create(
            asset.TenantId,
            "High trust system",
            null,
            EnvironmentClass.Server,
            InternetReachability.InternalNetwork,
            SecurityRequirementLevel.High,
            SecurityRequirementLevel.High,
            SecurityRequirementLevel.High
        );

        var result = _calculator.Calculate(vulnerability, asset, profile);

        result.EffectiveScore.Should().BeGreaterThan(vulnerability.CvssScore!.Value);
        result
            .EffectiveSeverity.Should()
            .BeOneOf(Severity.Medium, Severity.High, Severity.Critical);
    }

    [Fact]
    public void Calculate_ExplicitModifiedMetrics_AreAuthoritative()
    {
        var vulnerability = VulnerabilityDefinition.Create(
            "CVE-2026-4999",
            "Remote issue",
            "Desc",
            Severity.High,
            "MicrosoftDefender",
            8.8m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H"
        );
        var asset = Asset.Create(
            Guid.NewGuid(),
            "device-3",
            AssetType.Device,
            "Device",
            Criticality.High
        );
        var profile = AssetSecurityProfile.Create(
            asset.TenantId,
            "Locked down device",
            null,
            EnvironmentClass.Server,
            InternetReachability.Internet,
            SecurityRequirementLevel.Medium,
            SecurityRequirementLevel.Medium,
            SecurityRequirementLevel.Medium,
            CvssModifiedAttackVector.Local,
            CvssModifiedAttackComplexity.High,
            CvssModifiedPrivilegesRequired.High,
            CvssModifiedUserInteraction.Required,
            CvssModifiedScope.Unchanged,
            CvssModifiedImpact.Low,
            CvssModifiedImpact.Low,
            CvssModifiedImpact.Low
        );

        var result = _calculator.Calculate(vulnerability, asset, profile);

        result.EffectiveVector.Should().Contain("MAV:L");
        result.EffectiveVector.Should().Contain("MAC:H");
        result.EffectiveVector.Should().Contain("MPR:H");
        result.EffectiveVector.Should().Contain("MUI:R");
        result.EffectiveVector.Should().Contain("MC:L");
        result.EffectiveScore.Should().BeLessThan(vulnerability.CvssScore!.Value);
    }
}
