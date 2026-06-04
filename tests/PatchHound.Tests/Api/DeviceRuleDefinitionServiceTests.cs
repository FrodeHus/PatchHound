using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Services;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class DeviceRuleDefinitionServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;
    private readonly DeviceRuleDefinitionService _service;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public DeviceRuleDefinitionServiceTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        tenantContext.HasAccessToTenant(_tenantId).Returns(true);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(options, TestServiceProviderFactory.Create(tenantContext));
        _service = new DeviceRuleDefinitionService(_dbContext);
    }

    [Fact]
    public async Task ParseAndValidateAsync_ReturnsTypedDefinitionForValidDeviceRule()
    {
        var filter = BuildFilter("Name", "Device-A");
        var operations = new List<AssetRuleOperation>
        {
            new(DeviceRuleOperationTypes.SetCriticality, new Dictionary<string, string> { ["criticality"] = "High" }),
        };

        var result = await _service.ParseAndValidateAsync(
            _tenantId,
            DeviceRuleAssetTypes.Device,
            SerializeJson(filter),
            SerializeJson(operations),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Filter.Should().BeOfType<FilterGroup>();
        result.Operations.Should().ContainSingle()
            .Which.Type.Should().Be(DeviceRuleOperationTypes.SetCriticality);
    }

    [Fact]
    public async Task ParseAndValidateAsync_ReturnsActionableErrorForUnknownOperation()
    {
        var result = await _service.ParseAndValidateAsync(
            _tenantId,
            DeviceRuleAssetTypes.Device,
            SerializeJson(BuildFilter("Name", "Device-A")),
            SerializeJson(new List<AssetRuleOperation>
            {
                new("Noop", new Dictionary<string, string>()),
            }),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Unknown device rule operation type: Noop.");
    }

    [Fact]
    public async Task ParseAndValidateAsync_ReturnsActionableErrorForForeignTeamReference()
    {
        var otherTenantTeam = Team.Create(Guid.NewGuid(), "Other tenant");
        await _dbContext.Teams.AddAsync(otherTenantTeam);
        await _dbContext.SaveChangesAsync();

        var result = await _service.ParseAndValidateAsync(
            _tenantId,
            DeviceRuleAssetTypes.Software,
            SerializeJson(BuildFilter("Vendor", "Contoso")),
            SerializeJson(new List<AssetRuleOperation>
            {
                new(DeviceRuleOperationTypes.AssignOwnerTeam, new Dictionary<string, string> { ["teamId"] = otherTenantTeam.Id.ToString() }),
            }),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("AssignOwnerTeam references a team that does not belong to the active tenant.");
    }

    private static FilterNode BuildFilter(string field, string value) =>
        new FilterGroup(
            "AND",
            new List<FilterNode>
            {
                new FilterCondition(field, "Equals", value),
            });

    private static JsonElement SerializeJson(object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
