using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

        _dbContext = new PatchHoundDbContext(options, BuildServiceProvider(_tenantContext));

        var assetService = new AssetService(
            new AssetRepository(_dbContext),
            Substitute.For<IUnitOfWork>()
        );
        var assessmentService = new VulnerabilityAssessmentService(
            _dbContext,
            new EnvironmentalSeverityCalculator()
        );

        _controller = new AssetsController(
            _dbContext,
            assetService,
            assessmentService,
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

        await _dbContext.AddRangeAsync(device, software, standaloneSoftware);
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
                _tenantId,
                software.Id,
                "cpe:2.3:a:contoso:agent:1.0:*:*:*:*:*:*:*",
                CpeBindingMethod.DefenderDerived,
                MatchConfidence.High,
                "contoso",
                "agent",
                "1.0",
                new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.Zero)
            ),
            SoftwareCpeBinding.Create(
                _tenantId,
                standaloneSoftware.Id,
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

    private static IServiceProvider BuildServiceProvider(ITenantContext tenantContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);
        return services.BuildServiceProvider();
    }
}
