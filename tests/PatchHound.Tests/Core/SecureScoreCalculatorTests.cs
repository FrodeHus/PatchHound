using FluentAssertions;
using PatchHound.Core.Enums;
using PatchHound.Core.Services;

namespace PatchHound.Tests.Core;

public class SecureScoreCalculatorTests
{
    [Fact]
    public void AssetWithNoVulnerabilities_ShouldReturnZero()
    {
        var input = new SecureScoreCalculator.AssetInput(
            Guid.NewGuid(),
            DeviceValue: "Normal",
            HasSecurityProfile: true,
            Vulnerabilities: []
        );

        var result = SecureScoreCalculator.CalculateAssetScore(input);

        result.OverallScore.Should().Be(0m);
        result.VulnerabilityScore.Should().Be(0m);
        result.ConfigurationScore.Should().Be(0m);
        result.ActiveVulnerabilityCount.Should().Be(0);
    }

    [Fact]
    public void AssetWithCriticalVulnerability_ShouldScoreHigh()
    {
        var input = new SecureScoreCalculator.AssetInput(
            Guid.NewGuid(),
            DeviceValue: "Normal",
            HasSecurityProfile: true,
            Vulnerabilities:
            [
                new(Severity.Critical, 9.8m, IsOverdue: false),
            ]
        );

        var result = SecureScoreCalculator.CalculateAssetScore(input);

        result.OverallScore.Should().BeGreaterThan(30m);
        result.VulnerabilityScore.Should().BeGreaterThan(30m);
        result.ActiveVulnerabilityCount.Should().Be(1);
    }

    [Fact]
    public void HighDeviceValue_ShouldAmplifyScore()
    {
        var vulns = new[]
        {
            new SecureScoreCalculator.VulnerabilityInput(Severity.High, 7.5m, false),
        };

        var normalInput = new SecureScoreCalculator.AssetInput(
            Guid.NewGuid(), "Normal", true, vulns);
        var highInput = new SecureScoreCalculator.AssetInput(
            Guid.NewGuid(), "High", true, vulns);

        var normalScore = SecureScoreCalculator.CalculateAssetScore(normalInput);
        var highScore = SecureScoreCalculator.CalculateAssetScore(highInput);

        highScore.OverallScore.Should().BeGreaterThan(normalScore.OverallScore);
        highScore.DeviceValueWeight.Should().Be(1.5m);
        normalScore.DeviceValueWeight.Should().Be(1.0m);
    }

    [Fact]
    public void LowDeviceValue_ShouldReduceScore()
    {
        var vulns = new[]
        {
            new SecureScoreCalculator.VulnerabilityInput(Severity.High, 7.5m, false),
        };

        var normalInput = new SecureScoreCalculator.AssetInput(
            Guid.NewGuid(), "Normal", true, vulns);
        var lowInput = new SecureScoreCalculator.AssetInput(
            Guid.NewGuid(), "Low", true, vulns);

        var normalScore = SecureScoreCalculator.CalculateAssetScore(normalInput);
        var lowScore = SecureScoreCalculator.CalculateAssetScore(lowInput);

        lowScore.OverallScore.Should().BeLessThan(normalScore.OverallScore);
        lowScore.DeviceValueWeight.Should().Be(0.8m);
    }

    [Fact]
    public void MissingSecurityProfile_ShouldAddConfigurationPenalty()
    {
        var input = new SecureScoreCalculator.AssetInput(
            Guid.NewGuid(),
            DeviceValue: "Normal",
            HasSecurityProfile: false,
            Vulnerabilities: []
        );

        var result = SecureScoreCalculator.CalculateAssetScore(input);

        result.ConfigurationScore.Should().Be(10m);
        result.OverallScore.Should().BeGreaterThan(0m);
        result.Factors.Should().Contain(f => f.Name == "NoSecurityProfile");
    }

    [Fact]
    public void OverdueVulnerabilities_ShouldAddPenalty()
    {
        var vulns = new[]
        {
            new SecureScoreCalculator.VulnerabilityInput(Severity.High, 7.5m, true),
        };

        var noOverdueInput = new SecureScoreCalculator.AssetInput(
            Guid.NewGuid(), "Normal", true,
            [new(Severity.High, 7.5m, false)]);
        var overdueInput = new SecureScoreCalculator.AssetInput(
            Guid.NewGuid(), "Normal", true, vulns);

        var noOverdue = SecureScoreCalculator.CalculateAssetScore(noOverdueInput);
        var overdue = SecureScoreCalculator.CalculateAssetScore(overdueInput);

        overdue.OverallScore.Should().BeGreaterThan(noOverdue.OverallScore);
        overdue.Factors.Should().Contain(f => f.Name == "OverdueSla");
    }

    [Fact]
    public void ScoresShouldBeCappedAt100()
    {
        var vulns = Enumerable.Range(0, 50)
            .Select(_ => new SecureScoreCalculator.VulnerabilityInput(Severity.Critical, 10m, true))
            .ToList();

        var input = new SecureScoreCalculator.AssetInput(
            Guid.NewGuid(), "High", false, vulns);

        var result = SecureScoreCalculator.CalculateAssetScore(input);

        result.OverallScore.Should().BeLessThanOrEqualTo(100m);
        result.VulnerabilityScore.Should().BeLessThanOrEqualTo(100m);
    }

    [Fact]
    public void TenantScore_EmptyAssets_ShouldReturnZero()
    {
        var result = SecureScoreCalculator.CalculateTenantScore([], 40m);

        result.OverallScore.Should().Be(0m);
        result.AssetCount.Should().Be(0);
        result.AssetsAboveThreshold.Should().Be(0);
    }

    [Fact]
    public void TenantScore_ShouldWeightByDeviceValue()
    {
        var assetScores = new List<SecureScoreCalculator.AssetScoreResult>
        {
            new(Guid.NewGuid(), 80m, 80m, 0m, 1.5m, 5, []),
            new(Guid.NewGuid(), 20m, 20m, 0m, 0.8m, 1, []),
        };

        var result = SecureScoreCalculator.CalculateTenantScore(assetScores, 40m);

        // Weighted average: (80*1.5 + 20*0.8) / (1.5+0.8) = 136/2.3 ≈ 59.1
        result.OverallScore.Should().BeApproximately(59.1m, 0.5m);
        result.AssetsAboveThreshold.Should().Be(1);
    }

    [Fact]
    public void SerializeFactors_ShouldProduceCamelCaseJson()
    {
        var factors = new List<SecureScoreCalculator.ScoreFactor>
        {
            new("VulnerabilityExposure", "2 active vulnerabilities", 45.2m),
        };

        var json = SecureScoreCalculator.SerializeFactors(factors);

        json.Should().Contain("\"name\":");
        json.Should().Contain("\"description\":");
        json.Should().Contain("\"impact\":");
    }
}
