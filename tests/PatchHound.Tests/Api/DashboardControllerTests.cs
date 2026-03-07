using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Dashboard;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Tests.Api;

public class DashboardControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;
    private readonly DashboardController _controller;

    public DashboardControllerTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        tenantContext.CurrentUserId.Returns(Guid.NewGuid());

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(options, BuildServiceProvider(tenantContext));
        _controller = new DashboardController(_dbContext);
    }

    [Fact]
    public async Task GetSummary_ComputesRecurringCounts_AndTopRecurringAssets()
    {
        var recurringAsset = Asset.Create(_tenantId, "device-1", AssetType.Device, "Device 1", Criticality.High);
        recurringAsset.UpdateDeviceDetails("device-1.contoso.local", "Active", "Windows", "11", "High", DateTimeOffset.UtcNow, "10.0.0.1", "aad-1");

        var otherAsset = Asset.Create(_tenantId, "device-2", AssetType.Device, "Device 2", Criticality.Medium);
        otherAsset.UpdateDeviceDetails("device-2.contoso.local", "Active", "Windows", "11", "Medium", DateTimeOffset.UtcNow, "10.0.0.2", "aad-2");

        var recurringVulnerability = Vulnerability.Create(
            _tenantId,
            "CVE-2026-1000",
            "Recurring vulnerability",
            "Desc",
            Severity.Critical,
            "MicrosoftDefender",
            9.8m,
            null,
            DateTimeOffset.UtcNow.AddDays(-30)
        );

        var nonRecurringVulnerability = Vulnerability.Create(
            _tenantId,
            "CVE-2026-1001",
            "Single episode vulnerability",
            "Desc",
            Severity.High,
            "MicrosoftDefender",
            7.1m,
            null,
            DateTimeOffset.UtcNow.AddDays(-15)
        );

        var recurringLink = VulnerabilityAsset.Create(recurringVulnerability.Id, recurringAsset.Id, DateTimeOffset.UtcNow.AddDays(-3));
        var nonRecurringLink = VulnerabilityAsset.Create(nonRecurringVulnerability.Id, otherAsset.Id, DateTimeOffset.UtcNow.AddDays(-2));

        await _dbContext.AddRangeAsync(
            recurringAsset,
            otherAsset,
            recurringVulnerability,
            nonRecurringVulnerability,
            recurringLink,
            nonRecurringLink
        );

        await _dbContext.VulnerabilityAssetEpisodes.AddRangeAsync(
            VulnerabilityAssetEpisode.Create(_tenantId, recurringVulnerability.Id, recurringAsset.Id, 1, DateTimeOffset.UtcNow.AddDays(-20)),
            VulnerabilityAssetEpisode.Create(_tenantId, recurringVulnerability.Id, recurringAsset.Id, 2, DateTimeOffset.UtcNow.AddDays(-3)),
            VulnerabilityAssetEpisode.Create(_tenantId, nonRecurringVulnerability.Id, otherAsset.Id, 1, DateTimeOffset.UtcNow.AddDays(-2))
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetSummary(CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<DashboardSummaryDto>().Subject;

        payload.RecurringVulnerabilityCount.Should().Be(1);
        payload.RecurrenceRatePercent.Should().Be(50.0m);
        payload.TopRecurringVulnerabilities.Should().ContainSingle();
        payload.TopRecurringVulnerabilities[0].ExternalId.Should().Be("CVE-2026-1000");
        payload.TopRecurringVulnerabilities[0].ReappearanceCount.Should().Be(1);
        payload.TopRecurringAssets.Should().ContainSingle();
        payload.TopRecurringAssets[0].Name.Should().Be("device-1.contoso.local");
        payload.TopRecurringAssets[0].RecurringVulnerabilityCount.Should().Be(1);
    }

    public void Dispose() => _dbContext.Dispose();

    private static IServiceProvider BuildServiceProvider(ITenantContext tenantContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);
        return services.BuildServiceProvider();
    }
}
