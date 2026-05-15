using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PatchHound.Core.Constants;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Services.RiskScoring;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure;

namespace PatchHound.Tests.Infrastructure.Services;

public class RiskScoreServiceTruRiskCalibrationTests : IAsyncDisposable
{
    private static readonly Guid SourceSystemId = Guid.NewGuid();

    private readonly Guid _tenantId;
    private readonly global::PatchHound.Infrastructure.Data.PatchHoundDbContext _db;

    public RiskScoreServiceTruRiskCalibrationTests()
    {
        _db = TestDbContextFactory.CreateTenantContext(out _tenantId);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task RecalculateForTenantAsync_EmergencyCriticalSoftwareCase_ScoresCriticalOrAtLeastHigh()
    {
        var product = SoftwareProduct.Create("Contoso", "Emergency Agent", null);
        var device = Device.Create(_tenantId, SourceSystemId, "emergency-device", "Emergency Device", Criticality.Critical);
        await _db.AddRangeAsync(product, device);
        await _db.SaveChangesAsync();

        var critical = Vulnerability.Create(
            "nvd",
            "CVE-2026-TRU-0001",
            "Emergency critical",
            "desc",
            Severity.Critical,
            9.8m,
            null,
            DateTimeOffset.UtcNow);
        await SeedExposureAsync(product, device, critical, 9.8m);
        _db.VulnerabilityPatchAssessments.Add(CreatePatchAssessment(critical.Id, PatchUrgencyTier.Emergency));

        foreach (var index in Enumerable.Range(0, 49))
        {
            var high = Vulnerability.Create(
                "nvd",
                $"CVE-2026-TRU-H{index:0000}",
                $"High {index}",
                "desc",
                Severity.High,
                7.5m,
                null,
                DateTimeOffset.UtcNow);
            await SeedExposureAsync(product, device, high, 7.5m);
        }

        await _db.SaveChangesAsync();

        await RecalculateAsync();

        var score = await _db.SoftwareRiskScores.SingleAsync(item => item.SoftwareProductId == product.Id);
        score.OverallScore.Should().BeGreaterThanOrEqualTo(850m);
        RiskBand.FromScore(score.OverallScore).Should().Be("Critical");
        score.CalculationVersion.Should().Be("2-trurisk-inspired");
    }

    [Fact]
    public async Task RecalculateForTenantAsync_AffectedDeviceBreadth_IncreasesSoftwareScore()
    {
        var narrowProduct = SoftwareProduct.Create("Contoso", "Narrow Agent", null);
        var broadProduct = SoftwareProduct.Create("Contoso", "Broad Agent", null);
        await _db.AddRangeAsync(narrowProduct, broadProduct);
        await _db.SaveChangesAsync();

        var narrowDevice = Device.Create(_tenantId, SourceSystemId, "narrow-device", "Narrow Device", Criticality.Medium);
        await _db.Devices.AddAsync(narrowDevice);
        await SeedExposureAsync(narrowProduct, narrowDevice, CreateHighVulnerability("NARROW"), 7.5m);

        foreach (var index in Enumerable.Range(0, 8))
        {
            var device = Device.Create(
                _tenantId,
                SourceSystemId,
                $"broad-device-{index}",
                $"Broad Device {index}",
                Criticality.Medium);
            await _db.Devices.AddAsync(device);
            await SeedExposureAsync(broadProduct, device, CreateHighVulnerability($"BROAD-{index}"), 7.5m);
        }

        await _db.SaveChangesAsync();

        await RecalculateAsync();

        var narrowScore = await _db.SoftwareRiskScores.SingleAsync(item => item.SoftwareProductId == narrowProduct.Id);
        var broadScore = await _db.SoftwareRiskScores.SingleAsync(item => item.SoftwareProductId == broadProduct.Id);

        broadScore.AffectedDeviceCount.Should().BeGreaterThan(narrowScore.AffectedDeviceCount);
        broadScore.OverallScore.Should().BeGreaterThan(narrowScore.OverallScore);
    }

    [Fact]
    public async Task RecalculateForTenantAsync_KnownExploitedThreatAssessment_AppliesHighFloor()
    {
        var product = SoftwareProduct.Create("Contoso", "Known Exploited Agent", null);
        var device = Device.Create(_tenantId, SourceSystemId, "kev-device", "Known Exploited Device", Criticality.Low);
        var vulnerability = Vulnerability.Create(
            "nvd",
            "CVE-2026-TRU-KEV",
            "Known exploited low CVSS",
            "desc",
            Severity.Low,
            2.1m,
            null,
            DateTimeOffset.UtcNow);

        await _db.AddRangeAsync(product, device);
        await SeedExposureAsync(product, device, vulnerability, 2.1m);
        _db.ThreatAssessments.Add(ThreatAssessment.Create(
            vulnerability.Id,
            threatScore: 20m,
            technicalScore: 20m,
            exploitLikelihoodScore: 20m,
            threatActivityScore: 20m,
            epssScore: 0.05m,
            knownExploited: true,
            publicExploit: false,
            activeAlert: false,
            hasRansomwareAssociation: false,
            hasMalwareAssociation: false,
            factorsJson: "[]",
            calculationVersion: "test"));
        await _db.SaveChangesAsync();

        await RecalculateAsync();

        var score = await _db.SoftwareRiskScores.SingleAsync(item => item.SoftwareProductId == product.Id);
        score.OverallScore.Should().BeGreaterThanOrEqualTo(700m);
        RiskBand.FromScore(score.OverallScore).Should().Be("High");
    }

    [Fact]
    public async Task RecalculateForTenantAsync_DashboardTenantRisk_IncreasesWhenAssetScoresIncrease()
    {
        var product = SoftwareProduct.Create("Contoso", "Dashboard Agent", null);
        var device = Device.Create(_tenantId, SourceSystemId, "dashboard-device", "Dashboard Device", Criticality.Medium);
        await _db.AddRangeAsync(product, device);
        await SeedExposureAsync(product, device, CreateLowVulnerability("DASH-LOW"), 2.0m);
        await _db.SaveChangesAsync();

        await RecalculateAsync();
        var baselineRisk = await GetTenantRiskScoreAsync();

        await SeedExposureAsync(product, device, CreateHighVulnerability("DASH-HIGH"), 8.7m);
        await _db.SaveChangesAsync();

        await RecalculateAsync();
        var elevatedRisk = await GetTenantRiskScoreAsync();

        elevatedRisk.Should().BeGreaterThan(baselineRisk);
    }

    private async Task RecalculateAsync()
    {
        var service = new RiskScoreService(_db, Substitute.For<ILogger<RiskScoreService>>());
        await service.RecalculateForTenantAsync(_tenantId, CancellationToken.None);
    }

    private async Task<decimal> GetTenantRiskScoreAsync()
    {
        var service = new RiskScoreService(_db, Substitute.For<ILogger<RiskScoreService>>());
        return (await service.GetTenantRiskAsync(_tenantId, CancellationToken.None)).OverallScore;
    }

    private async Task<DeviceVulnerabilityExposure> SeedExposureAsync(
        SoftwareProduct product,
        Device device,
        Vulnerability vulnerability,
        decimal environmentalCvss)
    {
        _db.Vulnerabilities.Add(vulnerability);
        await _db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var installedSoftware = InstalledSoftware.Observe(
            _tenantId,
            device.Id,
            product.Id,
            SourceSystemId,
            "1.0.0",
            now);
        _db.InstalledSoftware.Add(installedSoftware);
        await _db.SaveChangesAsync();

        var exposure = DeviceVulnerabilityExposure.Observe(
            _tenantId,
            device.Id,
            vulnerability.Id,
            product.Id,
            installedSoftware.Id,
            "1.0.0",
            ExposureMatchSource.Product,
            now);
        _db.DeviceVulnerabilityExposures.Add(exposure);
        await _db.SaveChangesAsync();

        _db.ExposureAssessments.Add(ExposureAssessment.Create(
            _tenantId,
            exposure.Id,
            null,
            vulnerability.CvssScore ?? environmentalCvss,
            environmentalCvss,
            "test exposure",
            now));

        return exposure;
    }

    private static Vulnerability CreateHighVulnerability(string suffix) =>
        Vulnerability.Create(
            "nvd",
            $"CVE-2026-TRU-{suffix}",
            $"High {suffix}",
            "desc",
            Severity.High,
            7.5m,
            null,
            DateTimeOffset.UtcNow);

    private static Vulnerability CreateLowVulnerability(string suffix) =>
        Vulnerability.Create(
            "nvd",
            $"CVE-2026-TRU-{suffix}",
            $"Low {suffix}",
            "desc",
            Severity.Low,
            2.0m,
            null,
            DateTimeOffset.UtcNow);

    private static VulnerabilityPatchAssessment CreatePatchAssessment(Guid vulnerabilityId, string urgencyTier) =>
        VulnerabilityPatchAssessment.Create(
            vulnerabilityId,
            recommendation: "Patch",
            confidence: "High",
            summary: "summary",
            urgencyTier,
            urgencyTargetSla: "24h",
            urgencyReason: "test",
            similarVulnerabilities: "[]",
            compensatingControlsUntilPatched: "[]",
            references: "[]",
            aiProfileName: "test",
            rawOutput: null,
            assessedAt: DateTimeOffset.UtcNow);
}
