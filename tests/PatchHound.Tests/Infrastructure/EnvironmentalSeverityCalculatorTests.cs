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
        var vulnerability = Vulnerability.Create(
            "MicrosoftDefender",
            "CVE-2026-4000",
            "Remote issue",
            "Desc",
            Severity.Critical,
            9.8m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H",
            null
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
        var vulnerability = Vulnerability.Create(
            "MicrosoftDefender",
            "CVE-2026-4001",
            "Sensitive data issue",
            "Desc",
            Severity.Medium,
            5.5m,
            "CVSS:3.1/AV:L/AC:L/PR:L/UI:N/S:U/C:L/I:L/A:L",
            null
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
        var vulnerability = Vulnerability.Create(
            "MicrosoftDefender",
            "CVE-2026-4999",
            "Remote issue",
            "Desc",
            Severity.High,
            8.8m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H",
            null
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

    [Fact]
    public void Calculate_WithSecurityProfile_AssessmentCarriesModifiedScoreAndVersion()
    {
        var tenantId = Guid.NewGuid();
        var vulnerability = Vulnerability.Create(
            "MicrosoftDefender",
            "CVE-2026-5000",
            "Profile-sensitive issue",
            "Desc",
            Severity.High,
            8.8m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H",
            null
        );
        var asset = Asset.Create(
            tenantId,
            "device-4",
            AssetType.Device,
            "Device",
            Criticality.High
        );
        var profile = AssetSecurityProfile.Create(
            tenantId,
            "Constrained network",
            null,
            EnvironmentClass.Server,
            InternetReachability.LocalOnly,
            SecurityRequirementLevel.High,
            SecurityRequirementLevel.High,
            SecurityRequirementLevel.High
        );

        var result = _calculator.Calculate(vulnerability, asset, profile);

        result.AssetSecurityProfileId.Should().Be(profile.Id);
        result.EffectiveScore.Should().NotBeNull();
        result.EffectiveScore.Should().NotBe(vulnerability.CvssScore);
        result.EffectiveVector.Should().NotBe(vulnerability.CvssVector);

        var exposureAssessment = ExposureAssessment.Create(
            tenantId,
            Guid.NewGuid(),
            asset.Id,
            vulnerability.Id,
            profile.Id,
            result.EffectiveSeverity,
            result.EffectiveScore,
            result.EffectiveVector,
            result.FactorsJson,
            result.ReasonSummary,
            EnvironmentalSeverityCalculator.CalculationVersion
        );

        exposureAssessment.SecurityProfileId.Should().Be(profile.Id);
        exposureAssessment.Score.Should().Be(result.EffectiveScore);
        exposureAssessment.EffectiveSeverity.Should().Be(result.EffectiveSeverity);
        exposureAssessment.CalculationVersion.Should().Be(EnvironmentalSeverityCalculator.CalculationVersion);
    }
}
