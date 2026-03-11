using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Dashboard;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;

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

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(tenantContext)
        );
        _controller = new DashboardController(_dbContext);
    }

    [Fact]
    public async Task GetSummary_ComputesRecurringCounts_AndTopRecurringAssets()
    {
        var recurringAsset = Asset.Create(
            _tenantId,
            "device-1",
            AssetType.Device,
            "Device 1",
            Criticality.High
        );
        recurringAsset.UpdateDeviceDetails(
            "device-1.contoso.local",
            "Active",
            "Windows",
            "11",
            "High",
            DateTimeOffset.UtcNow,
            "10.0.0.1",
            "aad-1"
        );

        var otherAsset = Asset.Create(
            _tenantId,
            "device-2",
            AssetType.Device,
            "Device 2",
            Criticality.Medium
        );
        otherAsset.UpdateDeviceDetails(
            "device-2.contoso.local",
            "Active",
            "Windows",
            "11",
            "Medium",
            DateTimeOffset.UtcNow,
            "10.0.0.2",
            "aad-2"
        );

        var recurringDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-1000",
            "Recurring vulnerability",
            "Desc",
            Severity.Critical,
            "MicrosoftDefender",
            9.8m,
            null,
            DateTimeOffset.UtcNow.AddDays(-30)
        );
        var recurringTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            recurringDefinition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow
        );

        var nonRecurringDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-1001",
            "Single episode vulnerability",
            "Desc",
            Severity.High,
            "MicrosoftDefender",
            7.1m,
            null,
            DateTimeOffset.UtcNow.AddDays(-15)
        );
        var nonRecurringTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            nonRecurringDefinition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow
        );

        var recurringLink = VulnerabilityAsset.Create(
            recurringTenantVulnerability.Id,
            recurringAsset.Id,
            DateTimeOffset.UtcNow.AddDays(-3)
        );
        var nonRecurringLink = VulnerabilityAsset.Create(
            nonRecurringTenantVulnerability.Id,
            otherAsset.Id,
            DateTimeOffset.UtcNow.AddDays(-2)
        );

        await _dbContext.AddRangeAsync(
            recurringAsset,
            otherAsset,
            recurringDefinition,
            nonRecurringDefinition,
            recurringTenantVulnerability,
            nonRecurringTenantVulnerability,
            recurringLink,
            nonRecurringLink
        );

        await _dbContext.VulnerabilityAssetEpisodes.AddRangeAsync(
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                recurringTenantVulnerability.Id,
                recurringAsset.Id,
                1,
                DateTimeOffset.UtcNow.AddDays(-20)
            ),
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                recurringTenantVulnerability.Id,
                recurringAsset.Id,
                2,
                DateTimeOffset.UtcNow.AddDays(-3)
            ),
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                nonRecurringTenantVulnerability.Id,
                otherAsset.Id,
                1,
                DateTimeOffset.UtcNow.AddDays(-2)
            )
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

    [Fact]
    public async Task GetSummary_SeverityCounts_ExcludeResolvedVulnerabilities()
    {
        var openDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-2000",
            "Open vulnerability",
            "Desc",
            Severity.Critical,
            "MicrosoftDefender"
        );
        var openTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            openDefinition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow
        );
        var resolvedDefinition = VulnerabilityDefinition.Create(
            "CVE-2026-2001",
            "Resolved vulnerability",
            "Desc",
            Severity.Critical,
            "MicrosoftDefender"
        );
        var resolvedTenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            resolvedDefinition.Id,
            VulnerabilityStatus.Resolved,
            DateTimeOffset.UtcNow
        );

        await _dbContext.AddRangeAsync(
            openDefinition,
            resolvedDefinition,
            openTenantVulnerability,
            resolvedTenantVulnerability
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetSummary(CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<DashboardSummaryDto>().Subject;

        payload.VulnerabilitiesBySeverity["Critical"].Should().Be(1);
        payload.VulnerabilitiesByStatus["Resolved"].Should().Be(1);
    }

    [Fact]
    public async Task GetTrends_ExcludesResolvedVulnerabilities_OnResolutionDay()
    {
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-3000",
            "Recently resolved vulnerability",
            "Desc",
            Severity.High,
            "MicrosoftDefender"
        );
        var tenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            definition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow
        );
        var asset = Asset.Create(
            _tenantId,
            "device-trend-1",
            AssetType.Device,
            "Device Trend 1",
            Criticality.High
        );
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstSeenAt = new DateTimeOffset(
            today.AddDays(-2).ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero
        );
        var resolvedAt = new DateTimeOffset(
            today.ToDateTime(new TimeOnly(8, 0)),
            TimeSpan.Zero
        );

        await _dbContext.AddRangeAsync(definition, tenantVulnerability, asset);
        await _dbContext.VulnerabilityAssetEpisodes.AddAsync(
            VulnerabilityAssetEpisode.Create(
                _tenantId,
                tenantVulnerability.Id,
                asset.Id,
                1,
                firstSeenAt
            ),
            CancellationToken.None
        );
        await _dbContext.SaveChangesAsync();

        var episode = await _dbContext.VulnerabilityAssetEpisodes.SingleAsync();
        episode.Resolve(resolvedAt);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetTrends(CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<TrendDataDto>().Subject;

        var todayPoint = payload.Items.Single(item =>
            item.Date == today && item.Severity == Severity.High.ToString()
        );
        var yesterdayPoint = payload.Items.Single(item =>
            item.Date == today.AddDays(-1) && item.Severity == Severity.High.ToString()
        );

        todayPoint.Count.Should().Be(0);
        yesterdayPoint.Count.Should().Be(1);
    }

    public void Dispose() => _dbContext.Dispose();
}
