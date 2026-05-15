using System.Text.Json;
using FluentAssertions;
using PatchHound.Core.Enums;
using PatchHound.Core.Services.RiskScoring;

namespace PatchHound.Tests.Core.RiskScoring;

public class PatchHoundRiskScoringEngineTests
{
    [Theory]
    [InlineData(0, "None")]
    [InlineData(1, "Low")]
    [InlineData(499.99, "Low")]
    [InlineData(500, "Medium")]
    [InlineData(700, "High")]
    [InlineData(850, "Critical")]
    public void RiskBand_FromScore_UsesUnifiedTruRiskInspiredThresholds(decimal score, string expected)
    {
        RiskBand.FromScore(score).Should().Be(expected);
    }

    [Fact]
    public void CalculateSoftwareRisk_EmergencyCriticalAndManyHighs_IsCritical()
    {
        var deviceId = Guid.NewGuid();
        var inputs = new List<RiskExposureInput>
        {
            RiskExposureInput.Critical(deviceId, Guid.NewGuid(), 9.8m, isEmergency: true),
        };
        inputs.AddRange(Enumerable.Range(0, 49)
            .Select(_ => RiskExposureInput.High(deviceId, Guid.NewGuid(), 7.5m)));

        var result = PatchHoundRiskScoringEngine.CalculateSoftwareRisk(
            inputs,
            affectedDeviceCount: 1,
            highValueDeviceCount: 0);

        result.OverallScore.Should().Be(1000m);
        result.RiskBand.Should().Be("Critical");
    }

    [Fact]
    public void CalculateAssetRisk_SingleCriticalOnHighCriticalityAsset_IsAtLeastMedium()
    {
        var input = RiskExposureInput.Critical(
            Guid.NewGuid(),
            Guid.NewGuid(),
            9.5m,
            assetCriticality: Criticality.High);

        var result = PatchHoundRiskScoringEngine.CalculateAssetRisk([input], businessLabelWeight: 1m);

        result.OverallScore.Should().BeGreaterThanOrEqualTo(500m);
    }

    [Fact]
    public void CalculateSoftwareRisk_ZeroExposures_ReturnsNoneWithValidFactors()
    {
        var result = PatchHoundRiskScoringEngine.CalculateSoftwareRisk(
            [],
            affectedDeviceCount: 0,
            highValueDeviceCount: 0);

        result.OverallScore.Should().Be(0m);
        result.RiskBand.Should().Be("None");
        result.MaxDetectionScore.Should().Be(0m);
        result.CriticalCount.Should().Be(0);
        result.HighCount.Should().Be(0);
        result.MediumCount.Should().Be(0);
        result.LowCount.Should().Be(0);

        var factors = DeserializeFactors(result.FactorsJson);
        factors.Should().ContainSingle(factor =>
            factor.Name == "BaseComponent"
            && factor.Description.Length > 0
            && factor.Impact == 0m);
    }

    [Fact]
    public void CalculateSoftwareRisk_KnownExploitedLowCvss_AppliesUrgentThreatFloor()
    {
        var input = RiskExposureInput.Low(
            Guid.NewGuid(),
            Guid.NewGuid(),
            0.1m,
            knownExploited: true);

        var result = PatchHoundRiskScoringEngine.CalculateSoftwareRisk(
            [input],
            affectedDeviceCount: 1,
            highValueDeviceCount: 0);

        result.OverallScore.Should().BeGreaterThanOrEqualTo(700m);
        result.RiskBand.Should().Be("High");
    }

    [Fact]
    public void CalculateSoftwareRisk_PublicExploitOrHighEpss_RaisesDetectionScore()
    {
        var publicExploit = RiskExposureInput.Low(
            Guid.NewGuid(),
            Guid.NewGuid(),
            0.1m,
            publicExploit: true);
        var highEpss = RiskExposureInput.Low(
            Guid.NewGuid(),
            Guid.NewGuid(),
            0.1m,
            epssScore: 0.50m);

        var publicExploitResult = PatchHoundRiskScoringEngine.CalculateSoftwareRisk(
            [publicExploit],
            affectedDeviceCount: 1,
            highValueDeviceCount: 0);
        var highEpssResult = PatchHoundRiskScoringEngine.CalculateSoftwareRisk(
            [highEpss],
            affectedDeviceCount: 1,
            highValueDeviceCount: 0);

        publicExploitResult.MaxDetectionScore.Should().BeGreaterThanOrEqualTo(80m);
        highEpssResult.MaxDetectionScore.Should().BeGreaterThanOrEqualTo(80m);
    }

    [Fact]
    public void CalculateAssetRisk_CriticalityAndBusinessLabelMultipliers_IncreaseScore()
    {
        var vulnerabilityId = Guid.NewGuid();
        var lowCriticality = RiskExposureInput.High(
            Guid.NewGuid(),
            vulnerabilityId,
            7.0m,
            assetCriticality: Criticality.Low);
        var criticalCriticality = lowCriticality with { AssetCriticality = Criticality.Critical };
        var mediumCriticality = lowCriticality with { AssetCriticality = Criticality.Medium };

        var lowCriticalityResult = PatchHoundRiskScoringEngine.CalculateAssetRisk(
            [lowCriticality],
            businessLabelWeight: 1m);
        var criticalCriticalityResult = PatchHoundRiskScoringEngine.CalculateAssetRisk(
            [criticalCriticality],
            businessLabelWeight: 1m);
        var baseBusinessResult = PatchHoundRiskScoringEngine.CalculateAssetRisk(
            [mediumCriticality],
            businessLabelWeight: 1m);
        var weightedBusinessResult = PatchHoundRiskScoringEngine.CalculateAssetRisk(
            [mediumCriticality],
            businessLabelWeight: 1.5m);

        criticalCriticalityResult.OverallScore.Should().BeGreaterThan(lowCriticalityResult.OverallScore);
        weightedBusinessResult.OverallScore.Should().BeGreaterThan(baseBusinessResult.OverallScore);
    }

    [Fact]
    public void CalculateSoftwareRisk_ExtremeInputs_ClampsScoreAt1000()
    {
        var deviceId = Guid.NewGuid();
        var inputs = Enumerable.Range(0, 200)
            .Select(_ => RiskExposureInput.Critical(deviceId, Guid.NewGuid(), 10m, isEmergency: true))
            .ToList();

        var result = PatchHoundRiskScoringEngine.CalculateSoftwareRisk(
            inputs,
            affectedDeviceCount: 10000,
            highValueDeviceCount: 1000);

        result.OverallScore.Should().Be(1000m);
        result.RiskBand.Should().Be("Critical");
    }

    [Fact]
    public void CalculateSoftwareRisk_FactorsJson_UsesStableSoftwareComponentNames()
    {
        var input = RiskExposureInput.High(Guid.NewGuid(), Guid.NewGuid(), 7.5m);

        var result = PatchHoundRiskScoringEngine.CalculateSoftwareRisk(
            [input],
            affectedDeviceCount: 10,
            highValueDeviceCount: 2);

        var factors = DeserializeFactors(result.FactorsJson);

        factors.Should().OnlyContain(factor =>
            !string.IsNullOrWhiteSpace(factor.Name)
            && !string.IsNullOrWhiteSpace(factor.Description));
        factors.Select(factor => factor.Name).Should().BeEquivalentTo(
            "BaseComponent",
            "CountComponent",
            "BreadthComponent",
            "HighValueDeviceComponent",
            "FloorAdjustedScore");
        factors.Select(factor => factor.Impact).Should().OnlyContain(impact => impact >= 0m);
    }

    [Fact]
    public void CalculateAssetRisk_FactorsJson_UsesStableAssetComponentNames()
    {
        var input = RiskExposureInput.High(
            Guid.NewGuid(),
            Guid.NewGuid(),
            7.5m,
            assetCriticality: Criticality.High);

        var result = PatchHoundRiskScoringEngine.CalculateAssetRisk([input], businessLabelWeight: 1.25m);

        var factors = DeserializeFactors(result.FactorsJson);

        factors.Should().OnlyContain(factor =>
            !string.IsNullOrWhiteSpace(factor.Name)
            && !string.IsNullOrWhiteSpace(factor.Description));
        factors.Select(factor => factor.Name).Should().BeEquivalentTo(
            "BaseComponent",
            "CountComponent",
            "AssetCriticalityMultiplier",
            "BusinessLabelWeight",
            "FloorAdjustedScore");
        factors.Select(factor => factor.Impact).Should().OnlyContain(impact => impact >= 0m);
    }

    private static IReadOnlyList<RiskContributionFactor> DeserializeFactors(string factorsJson)
    {
        var factors = JsonSerializer.Deserialize<List<RiskContributionFactor>>(factorsJson);

        factors.Should().NotBeNull();
        return factors!;
    }
}
