using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services.Inventory;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure.Services;

public class DeviceRuleEvaluationServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _sourceSystemId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;

    public DeviceRuleEvaluationServiceTests()
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

    private Device CreateDevice(string externalId, string name, Criticality baseline)
    {
        return Device.Create(_tenantId, _sourceSystemId, externalId, name, baseline);
    }

    private DeviceRuleEvaluationService CreateService() =>
        new(
            _dbContext,
            new DeviceRuleFilterBuilder(_dbContext),
            new SoftwareRuleFilterBuilder(),
            Substitute.For<ILogger<DeviceRuleEvaluationService>>()
        );

    [Fact]
    public async Task EvaluateRulesAsync_SetCriticalityOperation_UpdatesCanonicalCriticalityAndProvenance()
    {
        var device = CreateDevice("device-1", "Tier0-DC-01", Criticality.Low);
        var rule = DeviceRule.Create(
            _tenantId,
            "Tier 0 critical devices",
            "Promote tier 0 devices to critical.",
            1,
            "Device",
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

        await _dbContext.AddRangeAsync(device, rule);
        await _dbContext.SaveChangesAsync();

        await CreateService().EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var refreshed = await _dbContext.Devices.SingleAsync(item => item.Id == device.Id);
        refreshed.Criticality.Should().Be(Criticality.Critical);
        refreshed.CriticalitySource.Should().Be("Rule");
        refreshed.CriticalityReason.Should().Be("Matched the Tier 0 naming rule.");
        refreshed.CriticalityRuleId.Should().Be(rule.Id);
        refreshed.CriticalityUpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task EvaluateRulesAsync_WhenDeviceStopsMatchingRule_ResetsToBaselineCriticality()
    {
        var device = CreateDevice("device-2", "Prod-Server-01", Criticality.Medium);
        device.SetCriticalityFromRule(Criticality.Critical, Guid.NewGuid(), "old");
        var rule = DeviceRule.Create(
            _tenantId,
            "Tier 0 critical devices",
            null,
            1,
            "Device",
            new PatchHound.Core.Models.FilterCondition("Name", "StartsWith", "Tier0-"),
            [
                new PatchHound.Core.Models.AssetRuleOperation(
                    "SetCriticality",
                    new Dictionary<string, string> { ["criticality"] = "Critical" }
                )
            ]
        );

        await _dbContext.AddRangeAsync(device, rule);
        await _dbContext.SaveChangesAsync();

        await CreateService().EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var refreshed = await _dbContext.Devices.SingleAsync(item => item.Id == device.Id);
        refreshed.Criticality.Should().Be(Criticality.Medium);
        refreshed.CriticalitySource.Should().Be("Default");
        refreshed.CriticalityRuleId.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateRulesAsync_DoesNotOverrideManualCriticality()
    {
        var device = CreateDevice("device-3", "Tier0-Workstation-01", Criticality.Low);
        device.SetCriticality(Criticality.High);
        var rule = DeviceRule.Create(
            _tenantId,
            "Tier 0 critical devices",
            null,
            1,
            "Device",
            new PatchHound.Core.Models.FilterCondition("Name", "StartsWith", "Tier0-"),
            [
                new PatchHound.Core.Models.AssetRuleOperation(
                    "SetCriticality",
                    new Dictionary<string, string> { ["criticality"] = "Critical" }
                )
            ]
        );

        await _dbContext.AddRangeAsync(device, rule);
        await _dbContext.SaveChangesAsync();

        await CreateService().EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var refreshed = await _dbContext.Devices.SingleAsync(item => item.Id == device.Id);
        refreshed.Criticality.Should().Be(Criticality.High);
        refreshed.CriticalitySource.Should().Be("ManualOverride");
    }

    [Fact]
    public async Task EvaluateCriticalityForDeviceAsync_AppliesFirstMatchingCriticalityRule()
    {
        var device = CreateDevice("device-4", "Tier0-Host-01", Criticality.Low);
        var rule = DeviceRule.Create(
            _tenantId,
            "Tier 0 critical devices",
            null,
            1,
            "Device",
            new PatchHound.Core.Models.FilterCondition("Name", "StartsWith", "Tier0-"),
            [
                new PatchHound.Core.Models.AssetRuleOperation(
                    "SetCriticality",
                    new Dictionary<string, string> { ["criticality"] = "Critical" }
                )
            ]
        );

        await _dbContext.AddRangeAsync(device, rule);
        await _dbContext.SaveChangesAsync();

        await CreateService().EvaluateCriticalityForDeviceAsync(_tenantId, device.Id, CancellationToken.None);

        var refreshed = await _dbContext.Devices.SingleAsync(item => item.Id == device.Id);
        refreshed.Criticality.Should().Be(Criticality.Critical);
        refreshed.CriticalitySource.Should().Be("Rule");
        refreshed.CriticalityRuleId.Should().Be(rule.Id);
    }

    [Fact]
    public async Task EvaluateRulesAsync_AssignSecurityProfileOperation_TracksAndReconcilesRuleOwnership()
    {
        var profile = SecurityProfile.Create(
            _tenantId,
            "Production",
            null,
            EnvironmentClass.Server,
            InternetReachability.Internet,
            SecurityRequirementLevel.High,
            SecurityRequirementLevel.High,
            SecurityRequirementLevel.High
        );
        var device = CreateDevice("device-5", "Prod-Server-01", Criticality.High);
        var rule = DeviceRule.Create(
            _tenantId,
            "Assign production profile",
            null,
            1,
            "Device",
            new PatchHound.Core.Models.FilterCondition("Name", "StartsWith", "Prod-"),
            [
                new PatchHound.Core.Models.AssetRuleOperation(
                    "AssignSecurityProfile",
                    new Dictionary<string, string> { ["securityProfileId"] = profile.Id.ToString() }
                )
            ]
        );

        await _dbContext.AddRangeAsync(profile, device, rule);
        await _dbContext.SaveChangesAsync();

        var service = CreateService();
        await service.EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var assigned = await _dbContext.Devices.SingleAsync(item => item.Id == device.Id);
        assigned.SecurityProfileId.Should().Be(profile.Id);
        assigned.SecurityProfileRuleId.Should().Be(rule.Id);

        assigned.UpdateDetails("Office-Server-01");
        await _dbContext.SaveChangesAsync();

        await service.EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var reconciled = await _dbContext.Devices.SingleAsync(item => item.Id == device.Id);
        reconciled.SecurityProfileId.Should().BeNull();
        reconciled.SecurityProfileRuleId.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateRulesAsync_AssignTeamOperation_TracksAndReconcilesRuleOwnership()
    {
        var team = Team.Create(_tenantId, "Customer Operators");
        var device = CreateDevice("device-6", "Ops-Workstation-01", Criticality.Medium);
        var rule = DeviceRule.Create(
            _tenantId,
            "Assign default team",
            null,
            1,
            "Device",
            new PatchHound.Core.Models.FilterCondition("Name", "StartsWith", "Ops-"),
            [
                new PatchHound.Core.Models.AssetRuleOperation(
                    "AssignTeam",
                    new Dictionary<string, string> { ["teamId"] = team.Id.ToString() }
                )
            ]
        );

        await _dbContext.AddRangeAsync(team, device, rule);
        await _dbContext.SaveChangesAsync();

        var service = CreateService();
        await service.EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var assigned = await _dbContext.Devices.SingleAsync(item => item.Id == device.Id);
        assigned.FallbackTeamId.Should().Be(team.Id);
        assigned.FallbackTeamRuleId.Should().Be(rule.Id);

        assigned.UpdateDetails("Laptop-01");
        await _dbContext.SaveChangesAsync();

        await service.EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var reconciled = await _dbContext.Devices.SingleAsync(item => item.Id == device.Id);
        reconciled.FallbackTeamId.Should().BeNull();
        reconciled.FallbackTeamRuleId.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateRulesAsync_AssignOwnerTeamOperation_TracksAndReconcilesDeviceRuleOwnership()
    {
        var team = Team.Create(_tenantId, "Infrastructure Owners");
        var device = CreateDevice("device-owner", "Ops-Server-01", Criticality.Medium);
        var rule = DeviceRule.Create(
            _tenantId,
            "Assign owner team",
            null,
            1,
            "Device",
            new PatchHound.Core.Models.FilterCondition("Name", "StartsWith", "Ops-"),
            [
                new PatchHound.Core.Models.AssetRuleOperation(
                    "AssignOwnerTeam",
                    new Dictionary<string, string> { ["teamId"] = team.Id.ToString() }
                )
            ]
        );

        await _dbContext.AddRangeAsync(team, device, rule);
        await _dbContext.SaveChangesAsync();

        var service = CreateService();
        await service.EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var assigned = await _dbContext.Devices.SingleAsync(item => item.Id == device.Id);
        assigned.OwnerTeamId.Should().Be(team.Id);
        assigned.OwnerTeamRuleId.Should().Be(rule.Id);

        assigned.UpdateDetails("Laptop-01");
        await _dbContext.SaveChangesAsync();

        await service.EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var reconciled = await _dbContext.Devices.SingleAsync(item => item.Id == device.Id);
        reconciled.OwnerTeamId.Should().BeNull();
        reconciled.OwnerTeamRuleId.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateRulesAsync_AssignOwnerTeamOperation_TracksAndReconcilesSoftwareRuleOwnership()
    {
        var team = Team.Create(_tenantId, "Software Owners");
        var product = SoftwareProduct.Create("Contoso", "Browser", null);
        var software = SoftwareTenantRecord.Create(_tenantId, null, product.Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var rule = DeviceRule.Create(
            _tenantId,
            "Assign software owner team",
            null,
            1,
            "Software",
            new PatchHound.Core.Models.FilterCondition("Vendor", "Equals", "Contoso"),
            [
                new PatchHound.Core.Models.AssetRuleOperation(
                    "AssignOwnerTeam",
                    new Dictionary<string, string> { ["teamId"] = team.Id.ToString() }
                )
            ]
        );

        await _dbContext.AddRangeAsync(team, product, software, rule);
        await _dbContext.SaveChangesAsync();

        var service = CreateService();
        await service.EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var assigned = await _dbContext.SoftwareTenantRecords.SingleAsync(item => item.Id == software.Id);
        assigned.OwnerTeamId.Should().Be(team.Id);
        assigned.OwnerTeamRuleId.Should().Be(rule.Id);

        rule.Update(rule.Name, rule.Description, rule.Enabled, "Software", new PatchHound.Core.Models.FilterCondition("Vendor", "Equals", "Fabrikam"), rule.ParseOperations());
        await _dbContext.SaveChangesAsync();

        await service.EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var reconciled = await _dbContext.SoftwareTenantRecords.SingleAsync(item => item.Id == software.Id);
        reconciled.OwnerTeamId.Should().BeNull();
        reconciled.OwnerTeamRuleId.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateRulesAsync_AssignOwnerTeamOperation_TracksAndReconcilesCloudApplicationRuleOwnership()
    {
        var team = Team.Create(_tenantId, "Application Owners");
        var application = CloudApplication.Create(
            _tenantId,
            _sourceSystemId,
            "app-object-1",
            "client-id-1",
            "Contoso Portal",
            null,
            false,
            []
        );
        var rule = DeviceRule.Create(
            _tenantId,
            "Assign application owner team",
            null,
            1,
            "Application",
            new PatchHound.Core.Models.FilterCondition("Name", "Equals", "Contoso Portal"),
            [
                new PatchHound.Core.Models.AssetRuleOperation(
                    "AssignOwnerTeam",
                    new Dictionary<string, string> { ["teamId"] = team.Id.ToString() }
                )
            ]
        );

        await _dbContext.AddRangeAsync(team, application, rule);
        await _dbContext.SaveChangesAsync();

        var service = CreateService();
        await service.EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var assigned = await _dbContext.CloudApplications.SingleAsync(item => item.Id == application.Id);
        assigned.OwnerTeamId.Should().Be(team.Id);
        assigned.OwnerTeamRuleId.Should().Be(rule.Id);

        rule.Update(
            rule.Name,
            rule.Description,
            rule.Enabled,
            "Application",
            new PatchHound.Core.Models.FilterCondition("Name", "Equals", "Fabrikam Portal"),
            rule.ParseOperations());
        await _dbContext.SaveChangesAsync();

        await service.EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var reconciled = await _dbContext.CloudApplications.SingleAsync(item => item.Id == application.Id);
        reconciled.OwnerTeamId.Should().BeNull();
        reconciled.OwnerTeamRuleId.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateRulesAsync_AssignScanProfile_CreatesAssignment()
    {
        var device = CreateDevice("device-7", "Server-01", Criticality.Medium);
        var scanProfileId = Guid.NewGuid();
        var profile = PatchHound.Core.Entities.AuthenticatedScans.ScanProfile.Create(
            _tenantId, "profile", "", "", Guid.NewGuid(), Guid.NewGuid(), true);
        var profileIdProp = typeof(PatchHound.Core.Entities.AuthenticatedScans.ScanProfile).GetProperty("Id")!;
        profileIdProp.SetValue(profile, scanProfileId);

        var rule = DeviceRule.Create(
            _tenantId, "Assign scan profile", null, 1, "Device",
            new PatchHound.Core.Models.FilterCondition("Name", "StartsWith", "Server-"),
            [
                new PatchHound.Core.Models.AssetRuleOperation(
                    "AssignScanProfile",
                    new Dictionary<string, string> { ["scanProfileId"] = scanProfileId.ToString() }
                )
            ]);

        await _dbContext.AddRangeAsync(device, profile, rule);
        await _dbContext.SaveChangesAsync();

        await CreateService().EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var assignment = await _dbContext.DeviceScanProfileAssignments.SingleAsync();
        assignment.DeviceId.Should().Be(device.Id);
        assignment.ScanProfileId.Should().Be(scanProfileId);
        assignment.AssignedByRuleId.Should().Be(rule.Id);
    }

    [Fact]
    public async Task EvaluateRulesAsync_AssignScanProfile_IdempotentOnSecondRun()
    {
        var device = CreateDevice("device-8", "Server-02", Criticality.Medium);
        var scanProfileId = Guid.NewGuid();
        var profile = PatchHound.Core.Entities.AuthenticatedScans.ScanProfile.Create(
            _tenantId, "profile2", "", "", Guid.NewGuid(), Guid.NewGuid(), true);
        typeof(PatchHound.Core.Entities.AuthenticatedScans.ScanProfile).GetProperty("Id")!
            .SetValue(profile, scanProfileId);

        var rule = DeviceRule.Create(
            _tenantId, "Assign scan", null, 1, "Device",
            new PatchHound.Core.Models.FilterCondition("Name", "StartsWith", "Server-"),
            [
                new PatchHound.Core.Models.AssetRuleOperation(
                    "AssignScanProfile",
                    new Dictionary<string, string> { ["scanProfileId"] = scanProfileId.ToString() }
                )
            ]);

        await _dbContext.AddRangeAsync(device, profile, rule);
        await _dbContext.SaveChangesAsync();

        var service = CreateService();
        await service.EvaluateRulesAsync(_tenantId, CancellationToken.None);
        await service.EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var count = await _dbContext.DeviceScanProfileAssignments.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task EvaluateRulesAsync_DoesNotCrossTenants()
    {
        var otherTenantId = Guid.NewGuid();
        var localDevice = CreateDevice("device-9", "Shared-Host-01", Criticality.Low);
        var otherDevice = Device.Create(
            otherTenantId,
            _sourceSystemId,
            "device-10",
            "Shared-Host-01",
            Criticality.Low);

        var localRule = DeviceRule.Create(
            _tenantId,
            "Local tenant rule",
            null,
            1,
            "Device",
            new PatchHound.Core.Models.FilterCondition("Name", "StartsWith", "Shared-"),
            [
                new PatchHound.Core.Models.AssetRuleOperation(
                    "SetCriticality",
                    new Dictionary<string, string> { ["criticality"] = "Critical" }
                )
            ]);

        await _dbContext.AddRangeAsync(localDevice, otherDevice, localRule);
        await _dbContext.SaveChangesAsync();

        await CreateService().EvaluateRulesAsync(_tenantId, CancellationToken.None);

        var local = await _dbContext.Devices.SingleAsync(d => d.Id == localDevice.Id);
        var other = await _dbContext.Devices.IgnoreQueryFilters().SingleAsync(d => d.Id == otherDevice.Id);
        local.Criticality.Should().Be(Criticality.Critical);
        other.Criticality.Should().Be(Criticality.Low);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
