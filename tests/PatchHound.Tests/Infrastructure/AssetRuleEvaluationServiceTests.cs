using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure;

public class AssetRuleEvaluationServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;

    public AssetRuleEvaluationServiceTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns([_tenantId]);
        tenantContext.HasAccessToTenant(_tenantId).Returns(true);
        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(tenantContext)
        );
    }

    [Fact]
    public async Task EvaluateRulesAsync_SetCriticalityOperation_UpdatesCanonicalCriticalityAndProvenance()
    {
        var asset = Asset.Create(
            _tenantId,
            "asset-1",
            AssetType.Device,
            "Tier0-DC-01",
            Criticality.Low
        );
        var rule = AssetRule.Create(
            _tenantId,
            "Tier 0 critical assets",
            "Promote tier 0 assets to critical.",
            1,
            new PatchHound.Core.Models.FilterCondition("Name", "StartsWith", "Tier0-"),
            [
                new PatchHound.Core.Models.AssetRuleOperation(
                    "SetCriticality",
                    new Dictionary<string, string>
                    {
                        ["criticality"] = "Critical",
                        ["reason"] = "Matched the Tier 0 naming rule."
                    }
                )
            ]
        );

        await _dbContext.AddRangeAsync(asset, rule);
        await _dbContext.SaveChangesAsync();

        var service = new AssetRuleEvaluationService(
            _dbContext,
            new AssetRuleFilterBuilder(_dbContext),
            Substitute.For<ILogger<AssetRuleEvaluationService>>()
        );

        await service.EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var refreshed = await _dbContext.Assets.SingleAsync(item => item.Id == asset.Id);
        refreshed.Criticality.Should().Be(Criticality.Critical);
        refreshed.CriticalitySource.Should().Be("Rule");
        refreshed.CriticalityReason.Should().Be("Matched the Tier 0 naming rule.");
        refreshed.CriticalityRuleId.Should().Be(rule.Id);
        refreshed.CriticalityUpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task EvaluateRulesAsync_WhenAssetStopsMatchingRule_ResetsToBaselineCriticality()
    {
        var asset = Asset.Create(
            _tenantId,
            "asset-2",
            AssetType.Device,
            "Prod-Server-01",
            Criticality.Medium
        );
        asset.SetCriticalityFromRule(Criticality.Critical, Guid.NewGuid(), "old");
        var rule = AssetRule.Create(
            _tenantId,
            "Tier 0 critical assets",
            null,
            1,
            new PatchHound.Core.Models.FilterCondition("Name", "StartsWith", "Tier0-"),
            [
                new PatchHound.Core.Models.AssetRuleOperation(
                    "SetCriticality",
                    new Dictionary<string, string> { ["criticality"] = "Critical" }
                )
            ]
        );

        await _dbContext.AddRangeAsync(asset, rule);
        await _dbContext.SaveChangesAsync();

        var service = new AssetRuleEvaluationService(
            _dbContext,
            new AssetRuleFilterBuilder(_dbContext),
            Substitute.For<ILogger<AssetRuleEvaluationService>>()
        );

        await service.EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var refreshed = await _dbContext.Assets.SingleAsync(item => item.Id == asset.Id);
        refreshed.Criticality.Should().Be(Criticality.Medium);
        refreshed.CriticalitySource.Should().Be("Default");
        refreshed.CriticalityRuleId.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateRulesAsync_DoesNotOverrideManualCriticality()
    {
        var asset = Asset.Create(
            _tenantId,
            "asset-3",
            AssetType.Device,
            "Tier0-Workstation-01",
            Criticality.Low
        );
        asset.SetCriticality(Criticality.High);
        var rule = AssetRule.Create(
            _tenantId,
            "Tier 0 critical assets",
            null,
            1,
            new PatchHound.Core.Models.FilterCondition("Name", "StartsWith", "Tier0-"),
            [
                new PatchHound.Core.Models.AssetRuleOperation(
                    "SetCriticality",
                    new Dictionary<string, string> { ["criticality"] = "Critical" }
                )
            ]
        );

        await _dbContext.AddRangeAsync(asset, rule);
        await _dbContext.SaveChangesAsync();

        var service = new AssetRuleEvaluationService(
            _dbContext,
            new AssetRuleFilterBuilder(_dbContext),
            Substitute.For<ILogger<AssetRuleEvaluationService>>()
        );

        await service.EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var refreshed = await _dbContext.Assets.SingleAsync(item => item.Id == asset.Id);
        refreshed.Criticality.Should().Be(Criticality.High);
        refreshed.CriticalitySource.Should().Be("ManualOverride");
    }

    [Fact]
    public async Task EvaluateCriticalityForAssetAsync_AppliesFirstMatchingCriticalityRule()
    {
        var asset = Asset.Create(
            _tenantId,
            "asset-4",
            AssetType.Device,
            "Tier0-Host-01",
            Criticality.Low
        );
        var rule = AssetRule.Create(
            _tenantId,
            "Tier 0 critical assets",
            null,
            1,
            new PatchHound.Core.Models.FilterCondition("Name", "StartsWith", "Tier0-"),
            [
                new PatchHound.Core.Models.AssetRuleOperation(
                    "SetCriticality",
                    new Dictionary<string, string> { ["criticality"] = "Critical" }
                )
            ]
        );

        await _dbContext.AddRangeAsync(asset, rule);
        await _dbContext.SaveChangesAsync();

        var service = new AssetRuleEvaluationService(
            _dbContext,
            new AssetRuleFilterBuilder(_dbContext),
            Substitute.For<ILogger<AssetRuleEvaluationService>>()
        );

        await service.EvaluateCriticalityForAssetAsync(_tenantId, asset.Id, CancellationToken.None);

        var refreshed = await _dbContext.Assets.SingleAsync(item => item.Id == asset.Id);
        refreshed.Criticality.Should().Be(Criticality.Critical);
        refreshed.CriticalitySource.Should().Be("Rule");
        refreshed.CriticalityRuleId.Should().Be(rule.Id);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
