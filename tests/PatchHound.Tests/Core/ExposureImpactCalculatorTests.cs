using FluentAssertions;
using PatchHound.Core.Enums;
using PatchHound.Core.Services;

namespace PatchHound.Tests.Core;

public class ExposureImpactCalculatorTests
{
    [Fact]
    public void SoftwareWithNoVulnerabilities_ShouldReturnZero()
    {
        var input = new ExposureImpactCalculator.SoftwareImpactInput(
            Guid.NewGuid(),
            DeviceCount: 10,
            HighValueDeviceCount: 0,
            Vulnerabilities: []);

        var result = ExposureImpactCalculator.CalculateSoftwareImpact(input);

        result.ImpactScore.Should().Be(0m);
    }

    [Fact]
    public void SoftwareWithNoDevices_ShouldReturnZero()
    {
        var input = new ExposureImpactCalculator.SoftwareImpactInput(
            Guid.NewGuid(),
            DeviceCount: 0,
            HighValueDeviceCount: 0,
            Vulnerabilities:
            [
                new(Severity.Critical, 9.8m),
            ]);

        var result = ExposureImpactCalculator.CalculateSoftwareImpact(input);

        result.ImpactScore.Should().Be(0m);
    }

    [Fact]
    public void SoftwareWithCriticalVuln_SingleDevice_ShouldScoreModerately()
    {
        var input = new ExposureImpactCalculator.SoftwareImpactInput(
            Guid.NewGuid(),
            DeviceCount: 1,
            HighValueDeviceCount: 0,
            Vulnerabilities:
            [
                new(Severity.Critical, 9.8m),
            ]);

        var result = ExposureImpactCalculator.CalculateSoftwareImpact(input);

        result.ImpactScore.Should().BeGreaterThan(0m);
        result.ImpactScore.Should().BeLessThanOrEqualTo(100m);
    }

    [Fact]
    public void MoreDevices_ShouldIncreaseImpact()
    {
        var vulns = new List<ExposureImpactCalculator.SoftwareVulnerabilityInput>
        {
            new(Severity.High, 7.5m),
        };

        var inputFew = new ExposureImpactCalculator.SoftwareImpactInput(
            Guid.NewGuid(), DeviceCount: 2, HighValueDeviceCount: 0, Vulnerabilities: vulns);
        var inputMany = new ExposureImpactCalculator.SoftwareImpactInput(
            Guid.NewGuid(), DeviceCount: 100, HighValueDeviceCount: 0, Vulnerabilities: vulns);

        var resultFew = ExposureImpactCalculator.CalculateSoftwareImpact(inputFew);
        var resultMany = ExposureImpactCalculator.CalculateSoftwareImpact(inputMany);

        resultMany.ImpactScore.Should().BeGreaterThan(resultFew.ImpactScore);
    }

    [Fact]
    public void HighValueDevices_ShouldIncreaseImpact()
    {
        var vulns = new List<ExposureImpactCalculator.SoftwareVulnerabilityInput>
        {
            new(Severity.High, 7.5m),
        };

        var inputNoHigh = new ExposureImpactCalculator.SoftwareImpactInput(
            Guid.NewGuid(), DeviceCount: 10, HighValueDeviceCount: 0, Vulnerabilities: vulns);
        var inputAllHigh = new ExposureImpactCalculator.SoftwareImpactInput(
            Guid.NewGuid(), DeviceCount: 10, HighValueDeviceCount: 10, Vulnerabilities: vulns);

        var resultNoHigh = ExposureImpactCalculator.CalculateSoftwareImpact(inputNoHigh);
        var resultAllHigh = ExposureImpactCalculator.CalculateSoftwareImpact(inputAllHigh);

        resultAllHigh.ImpactScore.Should().BeGreaterThan(resultNoHigh.ImpactScore);
    }

    [Fact]
    public void CriticalVulns_ShouldScoreHigherThanLow()
    {
        var criticalInput = new ExposureImpactCalculator.SoftwareImpactInput(
            Guid.NewGuid(),
            DeviceCount: 5,
            HighValueDeviceCount: 0,
            Vulnerabilities: [new(Severity.Critical, 9.8m)]);

        var lowInput = new ExposureImpactCalculator.SoftwareImpactInput(
            Guid.NewGuid(),
            DeviceCount: 5,
            HighValueDeviceCount: 0,
            Vulnerabilities: [new(Severity.Low, 2.0m)]);

        var criticalResult = ExposureImpactCalculator.CalculateSoftwareImpact(criticalInput);
        var lowResult = ExposureImpactCalculator.CalculateSoftwareImpact(lowInput);

        criticalResult.ImpactScore.Should().BeGreaterThan(lowResult.ImpactScore);
    }

    [Fact]
    public void ScoreShouldNeverExceed100()
    {
        var vulns = Enumerable.Range(0, 50)
            .Select(_ => new ExposureImpactCalculator.SoftwareVulnerabilityInput(Severity.Critical, 10m))
            .ToList();

        var input = new ExposureImpactCalculator.SoftwareImpactInput(
            Guid.NewGuid(), DeviceCount: 1000, HighValueDeviceCount: 1000, Vulnerabilities: vulns);

        var result = ExposureImpactCalculator.CalculateSoftwareImpact(input);

        result.ImpactScore.Should().BeLessThanOrEqualTo(100m);
    }

    [Fact]
    public void DeviceExposure_NoSoftware_ShouldReturnZero()
    {
        var result = ExposureImpactCalculator.CalculateDeviceExposure(Guid.NewGuid(), []);

        result.ExposureScore.Should().Be(0m);
    }

    [Fact]
    public void DeviceExposure_SumsInstalledSoftwareImpacts()
    {
        var installedSingle = new List<ExposureImpactCalculator.InstalledSoftwareInput>
        {
            new(Guid.NewGuid(), 20m),
        };
        var installedMultiple = new List<ExposureImpactCalculator.InstalledSoftwareInput>
        {
            new(Guid.NewGuid(), 20m),
            new(Guid.NewGuid(), 30m),
        };

        var resultSingle = ExposureImpactCalculator.CalculateDeviceExposure(Guid.NewGuid(), installedSingle);
        var resultMultiple = ExposureImpactCalculator.CalculateDeviceExposure(Guid.NewGuid(), installedMultiple);

        resultMultiple.ExposureScore.Should().BeGreaterThan(resultSingle.ExposureScore);
    }

    [Fact]
    public void DeviceExposure_ShouldNeverExceed100()
    {
        var installed = Enumerable.Range(0, 100)
            .Select(_ => new ExposureImpactCalculator.InstalledSoftwareInput(Guid.NewGuid(), 80m))
            .ToList();

        var result = ExposureImpactCalculator.CalculateDeviceExposure(Guid.NewGuid(), installed);

        result.ExposureScore.Should().BeLessThanOrEqualTo(100m);
    }

    [Fact]
    public void NullCvssScore_ShouldStillCalculateUsingSeverity()
    {
        var input = new ExposureImpactCalculator.SoftwareImpactInput(
            Guid.NewGuid(),
            DeviceCount: 5,
            HighValueDeviceCount: 0,
            Vulnerabilities:
            [
                new(Severity.High, null),
            ]);

        var result = ExposureImpactCalculator.CalculateSoftwareImpact(input);

        result.ImpactScore.Should().BeGreaterThan(0m);
    }
}
