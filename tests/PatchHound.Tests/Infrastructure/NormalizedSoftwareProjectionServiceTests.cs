using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure;

public class NormalizedSoftwareProjectionServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;
    private readonly NormalizedSoftwareProjectionService _service;

    public NormalizedSoftwareProjectionServiceTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(tenantContext)
        );
        _service = new NormalizedSoftwareProjectionService(
            _dbContext,
            new NormalizedSoftwareResolver(_dbContext)
        );
    }

    [Fact]
    public async Task SyncTenantAsync_CollapsesVersionsIntoSingleNormalizedSoftwareAndProjectsExposure()
    {
        var observedAt = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var deviceOne = Asset.Create(
            _tenantId,
            "device-1",
            AssetType.Device,
            "Device 1",
            Criticality.Medium
        );
        var deviceTwo = Asset.Create(
            _tenantId,
            "device-2",
            AssetType.Device,
            "Device 2",
            Criticality.Medium
        );
        var softwareOne = Asset.Create(
            _tenantId,
            "defender-1",
            AssetType.Software,
            "Contoso Agent",
            Criticality.Low
        );
        softwareOne.UpdateMetadata("""{"name":"Contoso Agent","vendor":"Contoso","version":"1.0"}""");
        var softwareTwo = Asset.Create(
            _tenantId,
            "defender-2",
            AssetType.Software,
            "Contoso Agent",
            Criticality.Low
        );
        softwareTwo.UpdateMetadata("""{"name":"Contoso Agent","vendor":"Contoso","version":"2.0"}""");

        var vulnerability = VulnerabilityDefinition.Create(
            "CVE-2026-0001",
            "Contoso Agent issue",
            "Description",
            Severity.High,
            "NVD"
        );
        var tenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            vulnerability.Id,
            VulnerabilityStatus.Open,
            observedAt
        );

        await _dbContext.AddRangeAsync(
            deviceOne,
            deviceTwo,
            softwareOne,
            softwareTwo,
            vulnerability,
            tenantVulnerability
        );
        await _dbContext.DeviceSoftwareInstallations.AddRangeAsync(
            DeviceSoftwareInstallation.Create(_tenantId, deviceOne.Id, softwareOne.Id, observedAt),
            DeviceSoftwareInstallation.Create(_tenantId, deviceTwo.Id, softwareTwo.Id, observedAt)
        );
        await _dbContext.DeviceSoftwareInstallationEpisodes.AddRangeAsync(
            DeviceSoftwareInstallationEpisode.Create(
                _tenantId,
                deviceOne.Id,
                softwareOne.Id,
                1,
                observedAt.AddDays(-10)
            ),
            DeviceSoftwareInstallationEpisode.Create(
                _tenantId,
                deviceTwo.Id,
                softwareTwo.Id,
                1,
                observedAt.AddDays(-5)
            )
        );
        await _dbContext.SoftwareVulnerabilityMatches.AddRangeAsync(
            SoftwareVulnerabilityMatch.Create(
                _tenantId,
                softwareOne.Id,
                vulnerability.Id,
                SoftwareVulnerabilityMatchMethod.CpeBinding,
                MatchConfidence.High,
                "match-one",
                observedAt
            ),
            SoftwareVulnerabilityMatch.Create(
                _tenantId,
                softwareTwo.Id,
                vulnerability.Id,
                SoftwareVulnerabilityMatchMethod.CpeBinding,
                MatchConfidence.High,
                "match-two",
                observedAt
            )
        );
        await _dbContext.SaveChangesAsync();

        await _service.SyncTenantAsync(_tenantId, CancellationToken.None);

        var normalizedSoftware = await _dbContext
            .NormalizedSoftware.IgnoreQueryFilters()
            .ToListAsync();
        normalizedSoftware.Should().ContainSingle();
        normalizedSoftware[0].CanonicalVendor.Should().Be("Contoso");
        normalizedSoftware[0].CanonicalName.Should().Be("Contoso Agent");
        normalizedSoftware[0].NormalizationMethod.Should().Be(SoftwareNormalizationMethod.Heuristic);

        var aliases = await _dbContext
            .NormalizedSoftwareAliases.IgnoreQueryFilters()
            .ToListAsync();
        aliases.Should().HaveCount(2);
        aliases.Select(item => item.NormalizedSoftwareId).Distinct().Should().ContainSingle();

        var tenantSoftware = await _dbContext
            .TenantSoftware.IgnoreQueryFilters()
            .Where(item => item.TenantId == _tenantId)
            .ToListAsync();
        tenantSoftware.Should().ContainSingle();

        var installations = await _dbContext
            .NormalizedSoftwareInstallations.IgnoreQueryFilters()
            .Where(item => item.TenantId == _tenantId)
            .ToListAsync();
        installations.Should().HaveCount(2);
        installations.Should().OnlyContain(item => item.IsActive);
        installations.Select(item => item.DetectedVersion).Should().BeEquivalentTo("1.0", "2.0");

        var projections = await _dbContext
            .NormalizedSoftwareVulnerabilityProjections.IgnoreQueryFilters()
            .Where(item => item.TenantId == _tenantId)
            .ToListAsync();
        projections.Should().ContainSingle();
        projections[0].TenantSoftwareId.Should().Be(tenantSoftware[0].Id);
        projections[0].AffectedInstallCount.Should().Be(2);
        projections[0].AffectedDeviceCount.Should().Be(2);
        projections[0].AffectedVersionCount.Should().Be(2);
        projections[0].BestMatchMethod.Should().Be(SoftwareVulnerabilityMatchMethod.CpeBinding);
        projections[0].BestConfidence.Should().Be(MatchConfidence.High);
        projections[0].ResolvedAt.Should().BeNull();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
