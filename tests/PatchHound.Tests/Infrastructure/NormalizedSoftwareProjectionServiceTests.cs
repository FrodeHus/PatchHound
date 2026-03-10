using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

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

        _dbContext = new PatchHoundDbContext(options, BuildServiceProvider(tenantContext));
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

        var vulnerability = Vulnerability.Create(
            _tenantId,
            "CVE-2026-0001",
            "Contoso Agent issue",
            "Description",
            Severity.High,
            "NVD"
        );

        await _dbContext.AddRangeAsync(deviceOne, deviceTwo, softwareOne, softwareTwo, vulnerability);
        await _dbContext.SoftwareCpeBindings.AddRangeAsync(
            SoftwareCpeBinding.Create(
                _tenantId,
                softwareOne.Id,
                "cpe:2.3:a:contoso:agent:1.0:*:*:*:*:*:*:*",
                CpeBindingMethod.Manual,
                MatchConfidence.High,
                "contoso",
                "agent",
                "1.0",
                observedAt
            ),
            SoftwareCpeBinding.Create(
                _tenantId,
                softwareTwo.Id,
                "cpe:2.3:a:contoso:agent:2.0:*:*:*:*:*:*:*",
                CpeBindingMethod.Manual,
                MatchConfidence.High,
                "contoso",
                "agent",
                "2.0",
                observedAt
            )
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
            .Where(item => item.TenantId == _tenantId)
            .ToListAsync();
        normalizedSoftware.Should().ContainSingle();
        normalizedSoftware[0].CanonicalVendor.Should().Be("contoso");
        normalizedSoftware[0].CanonicalName.Should().Be("agent");
        normalizedSoftware[0].NormalizationMethod.Should().Be(SoftwareNormalizationMethod.ExplicitCpe);

        var aliases = await _dbContext
            .NormalizedSoftwareAliases.IgnoreQueryFilters()
            .Where(item => item.TenantId == _tenantId)
            .ToListAsync();
        aliases.Should().HaveCount(2);
        aliases.Select(item => item.NormalizedSoftwareId).Distinct().Should().ContainSingle();

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

    private static IServiceProvider BuildServiceProvider(ITenantContext tenantContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);
        return services.BuildServiceProvider();
    }
}
