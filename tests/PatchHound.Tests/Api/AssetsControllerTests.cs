using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Assets;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Repositories;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class AssetsControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly AssetsController _controller;

    public AssetsControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        _tenantContext.CurrentUserId.Returns(Guid.NewGuid());

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );

        var assetService = new AssetService(
            new AssetRepository(_dbContext),
            Substitute.For<IUnitOfWork>()
        );
        var assessmentService = new VulnerabilityAssessmentService(
            _dbContext,
            new EnvironmentalSeverityCalculator()
        );
        var normalizedSoftwareProjectionService = new NormalizedSoftwareProjectionService(
            _dbContext,
            new NormalizedSoftwareResolver(_dbContext)
        );

        _controller = new AssetsController(
            _dbContext,
            assetService,
            assessmentService,
            normalizedSoftwareProjectionService,
            _tenantContext
        );
    }

    [Fact]
    public async Task Get_ReturnsSoftwareCpeBindings_ForSoftwareAssetsAndInstalledSoftware()
    {
        var device = Asset.Create(
            _tenantId,
            "device-1",
            AssetType.Device,
            "Device 1",
            Criticality.High
        );
        var software = Asset.Create(
            _tenantId,
            "software-1",
            AssetType.Software,
            "Contoso Agent",
            Criticality.Low
        );
        var standaloneSoftware = Asset.Create(
            _tenantId,
            "software-2",
            AssetType.Software,
            "Legacy Runtime",
            Criticality.Low
        );

        var normalizedAgent = NormalizedSoftware.Create(
            "agent",
            "contoso",
            "cpe:contoso:agent",
            "cpe:2.3:a:contoso:agent:*:*:*:*:*:*:*:*",
            SoftwareNormalizationMethod.ExplicitCpe,
            SoftwareNormalizationConfidence.High,
            new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.Zero)
        );
        var normalizedRuntime = NormalizedSoftware.Create(
            "runtime",
            "legacy",
            "cpe:legacy:runtime",
            "cpe:2.3:a:legacy:runtime:*:*:*:*:*:*:*:*",
            SoftwareNormalizationMethod.ExplicitCpe,
            SoftwareNormalizationConfidence.High,
            new DateTimeOffset(2026, 3, 8, 9, 30, 0, TimeSpan.Zero)
        );
        var tenantAgent = TenantSoftware.Create(
            _tenantId,
            null,
            normalizedAgent.Id,
            new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 8, 10, 0, 0, TimeSpan.Zero)
        );
        var tenantRuntime = TenantSoftware.Create(
            _tenantId,
            null,
            normalizedRuntime.Id,
            new DateTimeOffset(2026, 3, 8, 9, 30, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 8, 10, 0, 0, TimeSpan.Zero)
        );

        await _dbContext.AddRangeAsync(device, software, standaloneSoftware);
        await _dbContext.AddRangeAsync(normalizedAgent, normalizedRuntime, tenantAgent, tenantRuntime);
        await _dbContext.NormalizedSoftwareAliases.AddRangeAsync(
            NormalizedSoftwareAlias.Create(
                normalizedAgent.Id,
                SoftwareIdentitySourceSystem.Defender,
                software.ExternalId,
                "Contoso Agent",
                "Contoso",
                "1.0",
                SoftwareNormalizationConfidence.High,
                "Resolved via software CPE binding.",
                new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.Zero)
            ),
            NormalizedSoftwareAlias.Create(
                normalizedRuntime.Id,
                SoftwareIdentitySourceSystem.Defender,
                standaloneSoftware.ExternalId,
                "Legacy Runtime",
                "Legacy",
                "5.2",
                SoftwareNormalizationConfidence.High,
                "Resolved via software CPE binding.",
                new DateTimeOffset(2026, 3, 8, 9, 30, 0, 0, TimeSpan.Zero)
            )
        );
        await _dbContext.DeviceSoftwareInstallations.AddAsync(
            DeviceSoftwareInstallation.Create(
                _tenantId,
                device.Id,
                software.Id,
                new DateTimeOffset(2026, 3, 8, 10, 0, 0, TimeSpan.Zero)
            )
        );
        await _dbContext.SoftwareCpeBindings.AddRangeAsync(
            SoftwareCpeBinding.Create(
                normalizedAgent.Id,
                "cpe:2.3:a:contoso:agent:1.0:*:*:*:*:*:*:*",
                CpeBindingMethod.DefenderDerived,
                MatchConfidence.High,
                "contoso",
                "agent",
                "1.0",
                new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.Zero)
            ),
            SoftwareCpeBinding.Create(
                normalizedRuntime.Id,
                "cpe:2.3:a:legacy:runtime:5.2:*:*:*:*:*:*:*",
                CpeBindingMethod.Manual,
                MatchConfidence.Medium,
                "legacy",
                "runtime",
                "5.2",
                new DateTimeOffset(2026, 3, 8, 9, 30, 0, TimeSpan.Zero)
            )
        );
        await _dbContext.SaveChangesAsync();

        var deviceAction = await _controller.Get(device.Id, CancellationToken.None);
        var deviceResult = deviceAction.Result.Should().BeOfType<OkObjectResult>().Subject;
        var devicePayload = deviceResult.Value.Should().BeOfType<AssetDetailDto>().Subject;

        devicePayload.SoftwareInventory.Should().ContainSingle();
        devicePayload.SoftwareInventory[0].CpeBinding.Should().NotBeNull();
        devicePayload.SoftwareInventory[0].CpeBinding!.BindingMethod.Should().Be("DefenderDerived");
        devicePayload.SoftwareInventory[0].CpeBinding!.Confidence.Should().Be("High");

        var softwareAction = await _controller.Get(standaloneSoftware.Id, CancellationToken.None);
        var softwareResult = softwareAction.Result.Should().BeOfType<OkObjectResult>().Subject;
        var softwarePayload = softwareResult.Value.Should().BeOfType<AssetDetailDto>().Subject;

        softwarePayload.SoftwareCpeBinding.Should().NotBeNull();
        softwarePayload.SoftwareCpeBinding!.Cpe23Uri.Should().Contain("legacy:runtime");
        softwarePayload.SoftwareCpeBinding.BindingMethod.Should().Be("Manual");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
