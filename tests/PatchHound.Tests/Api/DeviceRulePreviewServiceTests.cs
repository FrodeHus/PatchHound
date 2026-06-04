using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

public class DeviceRulePreviewServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _sourceSystemId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;
    private readonly DeviceRulePreviewService _service;

    public DeviceRulePreviewServiceTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        tenantContext.HasAccessToTenant(_tenantId).Returns(true);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(tenantContext));
        var deviceFilterBuilder = new DeviceRuleFilterBuilder(_dbContext);
        _service = new DeviceRulePreviewService(
            _dbContext,
            new DeviceRuleEvaluationService(
                _dbContext,
                deviceFilterBuilder,
                new SoftwareRuleFilterBuilder(),
                Substitute.For<ILogger<DeviceRuleEvaluationService>>()),
            new SoftwareRuleFilterBuilder(),
            new CloudApplicationRuleFilterBuilder());
    }

    [Fact]
    public async Task PreviewAsync_ReturnsSoftwareMatches()
    {
        var device = Device.Create(_tenantId, _sourceSystemId, "device-1", "Device-A", Criticality.Medium);
        var matchingProduct = SoftwareProduct.Create("Contoso", "Browser", null);
        var otherProduct = SoftwareProduct.Create("Fabrikam", "Agent", null);
        var matchingTenantSoftware = SoftwareTenantRecord.Create(_tenantId, null, matchingProduct.Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var otherTenantSoftware = SoftwareTenantRecord.Create(_tenantId, null, otherProduct.Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await _dbContext.AddRangeAsync(
            device,
            matchingProduct,
            otherProduct,
            matchingTenantSoftware,
            otherTenantSoftware,
            InstalledSoftware.Observe(_tenantId, device.Id, matchingProduct.Id, _sourceSystemId, "1.0.0", DateTimeOffset.UtcNow),
            InstalledSoftware.Observe(_tenantId, device.Id, otherProduct.Id, _sourceSystemId, "2.0.0", DateTimeOffset.UtcNow));
        await _dbContext.SaveChangesAsync();

        var preview = await _service.PreviewAsync(
            _tenantId,
            DeviceRuleAssetTypes.Software,
            BuildFilter("Vendor", "Contoso"),
            CancellationToken.None);

        preview.Count.Should().Be(1);
        preview.Samples.Should().ContainSingle().Which.Id.Should().Be(matchingTenantSoftware.Id);
    }

    [Fact]
    public async Task PreviewAsync_ReturnsActionableErrorForInvalidSoftwareFilter()
    {
        var act = () => _service.PreviewAsync(
            _tenantId,
            DeviceRuleAssetTypes.Software,
            BuildFilter("BadField", "Contoso"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Unknown software filter field: BadField");
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
