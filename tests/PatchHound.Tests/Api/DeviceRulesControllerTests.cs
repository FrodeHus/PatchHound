using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models;
using PatchHound.Api.Models.DeviceRules;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Services.Inventory;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

// Phase 1 canonical cleanup (Task 14): parallel test suite for
// /api/device-rules. Seeds canonical Device + DeviceRule rows and
// exercises the DeviceRulesController end-to-end with the real
// DeviceRuleEvaluationService + DeviceRuleFilterBuilder.
public class DeviceRulesControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _sourceSystemId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly DeviceRulesController _controller;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public DeviceRulesControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        _tenantContext.CurrentUserId.Returns(Guid.NewGuid());
        _tenantContext.HasAccessToTenant(_tenantId).Returns(true);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );

        var snapshotResolver = new TenantSnapshotResolver(_dbContext);
        var riskRefreshService = new RiskRefreshService(
            _dbContext,
            new ExposureAssessmentService(_dbContext, new EnvironmentalSeverityCalculator()),
            new RiskScoreService(
                _dbContext,
                Substitute.For<Microsoft.Extensions.Logging.ILogger<RiskScoreService>>()
            )
        );
        var filterBuilder = new DeviceRuleFilterBuilder(_dbContext);
        var evaluationService = new DeviceRuleEvaluationService(
            _dbContext,
            filterBuilder,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<DeviceRuleEvaluationService>>()
        );

        _controller = new DeviceRulesController(
            _dbContext,
            _tenantContext,
            evaluationService,
            filterBuilder,
            riskRefreshService
        );
    }

    [Fact]
    public async Task Create_PersistsRuleAndReturnsDto()
    {
        var filter = BuildNameFilter("Device-A");
        var operations = new List<AssetRuleOperation>
        {
            new("SetCriticality", new Dictionary<string, string> { ["criticality"] = "High" }),
        };

        var action = await _controller.Create(
            new CreateDeviceRuleRequest(
                "Critical workstations",
                "Match anything named Device-A",
                SerializeJson(filter),
                SerializeJson(operations)
            ),
            CancellationToken.None
        );

        var created = action.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var dto = created.Value.Should().BeOfType<DeviceRuleDto>().Subject;
        dto.Name.Should().Be("Critical workstations");
        dto.Priority.Should().Be(1);

        var stored = await _dbContext.DeviceRules.SingleAsync(r => r.Id == dto.Id);
        stored.Name.Should().Be("Critical workstations");
    }

    [Fact]
    public async Task Create_RejectsUnknownBusinessLabel()
    {
        var filter = BuildNameFilter("Device-A");
        var operations = new List<AssetRuleOperation>
        {
            new(
                "AssignBusinessLabel",
                new Dictionary<string, string> { ["businessLabelId"] = Guid.NewGuid().ToString() }
            ),
        };

        var action = await _controller.Create(
            new CreateDeviceRuleRequest(
                "Bogus",
                null,
                SerializeJson(filter),
                SerializeJson(operations)
            ),
            CancellationToken.None
        );

        action.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task List_ReturnsRulesOrderedByPriority()
    {
        await _dbContext.AddRangeAsync(
            CreateSimpleRule("Priority 1", priority: 1),
            CreateSimpleRule("Priority 2", priority: 2)
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(new PaginationQuery(), CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<PagedResponse<DeviceRuleDto>>().Subject;
        payload.TotalCount.Should().Be(2);
        payload.Items.Select(item => item.Name).Should().ContainInOrder("Priority 1", "Priority 2");
    }

    [Fact]
    public async Task Preview_CountsMatchingCanonicalDevices()
    {
        var matching = CreateDevice("match", "Device-A", Criticality.Medium);
        var other = CreateDevice("other", "Device-B", Criticality.Medium);
        await _dbContext.AddRangeAsync(matching, other);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Preview(
            new PreviewDeviceRuleFilterRequest(SerializeJson(BuildNameFilter("Device-A"))),
            CancellationToken.None
        );

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var preview = ok.Value.Should().BeOfType<DeviceRulePreviewDto>().Subject;
        preview.Count.Should().Be(1);
        preview.Samples.Should().ContainSingle().Which.Id.Should().Be(matching.Id);
    }

    [Fact]
    public async Task Run_ApplyRuleAssignsBusinessLabelAndClearsOnDelete()
    {
        var label = BusinessLabel.Create(_tenantId, "Critical Infra", null, null);
        var device = CreateDevice("device-1", "Device-A", Criticality.Low);
        await _dbContext.AddRangeAsync(label, device);
        await _dbContext.SaveChangesAsync();

        var filter = BuildNameFilter("Device-A");
        var operations = new List<AssetRuleOperation>
        {
            new(
                "AssignBusinessLabel",
                new Dictionary<string, string> { ["businessLabelId"] = label.Id.ToString() }
            ),
        };
        var createAction = await _controller.Create(
            new CreateDeviceRuleRequest("Tag Device-A", null, SerializeJson(filter), SerializeJson(operations)),
            CancellationToken.None
        );
        var createdRule = createAction.Result.Should().BeOfType<CreatedAtActionResult>()
            .Subject.Value.Should().BeOfType<DeviceRuleDto>().Subject;

        var runAction = await _controller.Run(CancellationToken.None);
        runAction.Should().BeOfType<NoContentResult>();

        var linksAfterRun = await _dbContext.DeviceBusinessLabels
            .Where(link => link.DeviceId == device.Id)
            .ToListAsync();
        linksAfterRun.Should().ContainSingle()
            .Which.AssignedByRuleId.Should().Be(createdRule.Id);

        var deleteAction = await _controller.Delete(createdRule.Id, CancellationToken.None);
        deleteAction.Should().BeOfType<NoContentResult>();

        var linksAfterDelete = await _dbContext.DeviceBusinessLabels
            .Where(link => link.DeviceId == device.Id)
            .ToListAsync();
        linksAfterDelete.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_PreservesManualBusinessLabelAssignment()
    {
        var label = BusinessLabel.Create(_tenantId, "Revenue", null, null);
        var device = CreateDevice("device-1", "Device-A", Criticality.Low);
        await _dbContext.AddRangeAsync(
            label,
            device,
            DeviceBusinessLabel.CreateManual(_tenantId, device.Id, label.Id, assignedBy: null)
        );
        await _dbContext.SaveChangesAsync();

        var filter = BuildNameFilter("Device-A");
        var operations = new List<AssetRuleOperation>
        {
            new(
                "AssignBusinessLabel",
                new Dictionary<string, string> { ["businessLabelId"] = label.Id.ToString() }
            ),
        };
        var createAction = await _controller.Create(
            new CreateDeviceRuleRequest("Tag Device-A", null, SerializeJson(filter), SerializeJson(operations)),
            CancellationToken.None
        );
        var createdRule = createAction.Result.Should().BeOfType<CreatedAtActionResult>()
            .Subject.Value.Should().BeOfType<DeviceRuleDto>().Subject;

        await _controller.Run(CancellationToken.None);
        await _controller.Delete(createdRule.Id, CancellationToken.None);

        var linksAfterDelete = await _dbContext.DeviceBusinessLabels
            .Where(link => link.DeviceId == device.Id)
            .ToListAsync();
        linksAfterDelete.Should().ContainSingle()
            .Which.SourceType.Should().Be(DeviceBusinessLabel.ManualSourceType);
    }

    [Fact]
    public async Task Reorder_UpdatesRulePriorities()
    {
        var rule1 = CreateSimpleRule("A", priority: 1);
        var rule2 = CreateSimpleRule("B", priority: 2);
        await _dbContext.AddRangeAsync(rule1, rule2);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Reorder(
            new ReorderDeviceRulesRequest(new List<Guid> { rule2.Id, rule1.Id }),
            CancellationToken.None
        );
        action.Should().BeOfType<NoContentResult>();

        var stored = await _dbContext.DeviceRules
            .OrderBy(r => r.Priority)
            .ToListAsync();
        stored[0].Id.Should().Be(rule2.Id);
        stored[1].Id.Should().Be(rule1.Id);
    }

    [Fact]
    public async Task Get_ReturnsNotFoundForUnknownRule()
    {
        var action = await _controller.Get(Guid.NewGuid(), CancellationToken.None);
        action.Result.Should().BeOfType<NotFoundResult>();
    }

    private Device CreateDevice(string externalId, string name, Criticality criticality)
    {
        return Device.Create(_tenantId, _sourceSystemId, externalId, name, criticality);
    }

    private DeviceRule CreateSimpleRule(string name, int priority)
    {
        return DeviceRule.Create(
            _tenantId,
            name,
            null,
            priority,
            BuildNameFilter("unused"),
            new List<AssetRuleOperation>
            {
                new("SetCriticality", new Dictionary<string, string> { ["criticality"] = "High" }),
            }
        );
    }

    private static FilterNode BuildNameFilter(string name) =>
        new FilterGroup(
            "AND",
            new List<FilterNode>
            {
                new FilterCondition("Name", "Equals", name),
            }
        );

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
