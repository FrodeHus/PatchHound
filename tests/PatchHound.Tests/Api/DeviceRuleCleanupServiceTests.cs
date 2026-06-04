using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Services;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services.Inventory;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class DeviceRuleCleanupServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _sourceSystemId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;
    private readonly DeviceRuleCleanupService _service;

    public DeviceRuleCleanupServiceTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        tenantContext.HasAccessToTenant(_tenantId).Returns(true);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(tenantContext));
        _service = new DeviceRuleCleanupService(
            _dbContext,
            new DeviceRuleFilterBuilder(_dbContext),
            new SoftwareRuleFilterBuilder(),
            new CloudApplicationRuleFilterBuilder());
    }

    [Fact]
    public async Task ResetDeletedRuleEffectsAsync_ClearsOnlyRuleAssignedDeviceBusinessLabels()
    {
        var label = BusinessLabel.Create(_tenantId, "Critical Infra", null, null);
        var device = Device.Create(_tenantId, _sourceSystemId, "device-1", "Device-A", Criticality.Low);
        var rule = DeviceRule.Create(
            _tenantId,
            "Tag Device-A",
            null,
            1,
            DeviceRuleAssetTypes.Device,
            BuildFilter("Name", "Device-A"),
            new List<AssetRuleOperation>
            {
                new(DeviceRuleOperationTypes.AssignBusinessLabel, new Dictionary<string, string> { ["businessLabelId"] = label.Id.ToString() }),
            });
        await _dbContext.AddRangeAsync(
            label,
            device,
            DeviceBusinessLabel.CreateManual(_tenantId, device.Id, label.Id, assignedBy: null),
            DeviceBusinessLabel.CreateRule(_tenantId, device.Id, label.Id, rule.Id));
        await _dbContext.SaveChangesAsync();

        await _service.ResetDeletedRuleEffectsAsync(_tenantId, rule, rule.ParseOperations(), CancellationToken.None);

        var links = await _dbContext.DeviceBusinessLabels
            .Where(link => link.DeviceId == device.Id)
            .ToListAsync();
        links.Should().ContainSingle()
            .Which.SourceType.Should().Be(DeviceBusinessLabel.ManualSourceType);
    }

    private static FilterNode BuildFilter(string field, string value) =>
        new FilterGroup(
            "AND",
            new List<FilterNode>
            {
                new FilterCondition(field, "Equals", value),
            });

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
