using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Devices;
using PatchHound.Api.Models.Software;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Services.Inventory;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

// Phase 1 canonical cleanup (Task 13): parallel test suite for
// /api/devices. Seeds canonical Device rows directly and exercises the
// DeviceService + DeviceDetailQueryService + DevicesController stack.
// Where the test seeds risk-assessment data via legacy AssetId-keyed
// tables, it also seeds a paired Asset row with `Asset.Id == Device.Id`
// (via reflection) so the existing Asset-navigation query filters pass.
// Phase 5 will rewire those tables off the Asset navigation and the
// bridge stubs will be dropped.
public class DevicesControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _sourceSystemId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly DevicesController _controller;

    public DevicesControllerTests()
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

        var deviceService = new DeviceService(_dbContext);
        var snapshotResolver = new TenantSnapshotResolver(_dbContext);
        var riskRefreshService = new RiskRefreshService(
            _dbContext,
            new ExposureAssessmentService(_dbContext, new EnvironmentalSeverityCalculator()),
            new RiskScoreService(
                _dbContext,
                Substitute.For<Microsoft.Extensions.Logging.ILogger<RiskScoreService>>()
            ),
            Substitute.For<MaterializedViewRefreshService>(_dbContext)
        );
        var workflowService = new RemediationWorkflowService(_dbContext);
        var notificationService = Substitute.For<INotificationService>();
        var patchingTaskService = new PatchingTaskService(
            _dbContext,
            new SlaService(),
            workflowService,
            notificationService
        );
        var approvalTaskService = new ApprovalTaskService(
            _dbContext,
            notificationService,
            Substitute.For<IRealTimeNotifier>(),
            workflowService,
            patchingTaskService,
            new ApprovedVulnerabilityRemediationService(_dbContext)
        );
        var remediationDecisionService = new RemediationDecisionService(
            _dbContext,
            approvalTaskService,
            workflowService,
            patchingTaskService
        );
        var remediationTaskQueryService = new PatchHound.Api.Services.RemediationTaskQueryService(
            _dbContext
        );
        var detailQueryService = new PatchHound.Api.Services.DeviceDetailQueryService(
            _dbContext,
            remediationTaskQueryService
        );
        var deviceRuleEvaluationService = new DeviceRuleEvaluationService(
            _dbContext,
            new DeviceRuleFilterBuilder(_dbContext),
            new SoftwareRuleFilterBuilder(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<DeviceRuleEvaluationService>>()
        );

        _controller = new DevicesController(
            _dbContext,
            deviceService,
            _tenantContext,
            snapshotResolver,
            detailQueryService,
            riskRefreshService,
            deviceRuleEvaluationService
        );
    }

    [Fact]
    public async Task List_ReturnsCanonicalDevicesWithBusinessLabelsAndTags()
    {
        var label = BusinessLabel.Create(_tenantId, "Critical Infra", "desc", "#ff0000");
        var deviceA = CreateDevice("device-a", "Device A", Criticality.High);
        var deviceB = CreateDevice("device-b", "Device B", Criticality.Medium);
        deviceA.UpdateInventoryDetails(
            computerDnsName: "device-a.contoso.local",
            healthStatus: "Active",
            osPlatform: "Windows",
            osVersion: "11",
            externalRiskLabel: "High",
            lastSeenAt: DateTimeOffset.UtcNow,
            lastIpAddress: "10.0.0.1",
            aadDeviceId: "aad-a",
            groupId: "g1",
            groupName: "Workstations",
            exposureLevel: "Medium",
            isAadJoined: true,
            onboardingStatus: "Onboarded",
            deviceValue: "Normal"
        );

        await _dbContext.AddRangeAsync(
            label,
            deviceA,
            deviceB,
            DeviceBusinessLabel.Create(_tenantId, deviceA.Id, label.Id),
            DeviceTag.Create(_tenantId, deviceA.Id, "env", "prod")
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(
            new DeviceFilterQuery(),
            new PaginationQuery(),
            CancellationToken.None
        );

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<PagedResponse<DeviceDto>>().Subject;
        payload.TotalCount.Should().Be(2);
        payload.Items.Should().HaveCount(2);

        var deviceADto = payload.Items.Single(d => d.Id == deviceA.Id);
        deviceADto.Name.Should().Be("device-a.contoso.local");
        deviceADto.Criticality.Should().Be("High");
        deviceADto.GroupName.Should().Be("Workstations");
        deviceADto.HealthStatus.Should().Be("Active");
        deviceADto.RiskBand.Should().Be("None");
        deviceADto.OnboardingStatus.Should().Be("Onboarded");
        deviceADto.DeviceValue.Should().Be("Normal");
        deviceADto.Tags.Should().ContainSingle().Which.Should().Be("prod");
        deviceADto.BusinessLabels.Should().ContainSingle().Which.Name.Should().Be("Critical Infra");
    }

    [Fact]
    public async Task List_FiltersByCriticalityOwnerAndBusinessLabel()
    {
        var label = BusinessLabel.Create(_tenantId, "Prod", null, null);
        var userId = Guid.NewGuid();
        var deviceA = CreateDevice("device-a", "Device A", Criticality.High);
        var deviceB = CreateDevice("device-b", "Device B", Criticality.Low);
        deviceA.AssignOwner(userId);

        await _dbContext.AddRangeAsync(
            label,
            deviceA,
            deviceB,
            DeviceBusinessLabel.Create(_tenantId, deviceA.Id, label.Id)
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(
            new DeviceFilterQuery(
                Criticality: "High",
                BusinessLabelId: label.Id,
                OwnerId: userId
            ),
            new PaginationQuery(),
            CancellationToken.None
        );

        var payload = action.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<PagedResponse<DeviceDto>>().Subject;
        payload.TotalCount.Should().Be(1);
        payload.Items.Single().Id.Should().Be(deviceA.Id);
    }

    [Fact]
    public async Task List_FiltersByPatchHoundRiskBand()
    {
        var deviceA = CreateDevice("device-a", "Device A", Criticality.High);
        var deviceB = CreateDevice("device-b", "Device B", Criticality.Low);

        await _dbContext.AddRangeAsync(
            deviceA,
            deviceB,
            DeviceRiskScore.Create(
                _tenantId,
                deviceA.Id,
                710m,
                710m,
                0,
                1,
                0,
                0,
                1,
                "[]",
                RiskScoreService.CalculationVersion
            ),
            DeviceRiskScore.Create(
                _tenantId,
                deviceB.Id,
                430m,
                430m,
                0,
                0,
                1,
                0,
                1,
                "[]",
                RiskScoreService.CalculationVersion
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(
            new DeviceFilterQuery(RiskBand: "High"),
            new PaginationQuery(),
            CancellationToken.None
        );

        var payload = action.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<PagedResponse<DeviceDto>>().Subject;
        payload.TotalCount.Should().Be(1);
        var item = payload.Items.Single();
        item.Id.Should().Be(deviceA.Id);
        item.RiskBand.Should().Be("High");
    }

    [Fact]
    public async Task List_CountsOnlyOpenVulnerabilities()
    {
        var device = CreateDevice("device-a", "Device A", Criticality.High);
        var openVuln = Vulnerability.Create("nvd", "CVE-2026-OPEN", "Open", "desc", Severity.High, 7.5m, null, DateTimeOffset.UtcNow);
        var resolvedVuln = Vulnerability.Create("nvd", "CVE-2026-RESOLVED", "Resolved", "desc", Severity.Critical, 9.1m, null, DateTimeOffset.UtcNow);
        var install = InstalledSoftware.Observe(_tenantId, device.Id, Guid.NewGuid(), _sourceSystemId, "1.0", DateTimeOffset.UtcNow);

        await _dbContext.AddRangeAsync(device, openVuln, resolvedVuln, install);
        await _dbContext.SaveChangesAsync();

        var openExposure = DeviceVulnerabilityExposure.Observe(
            _tenantId,
            device.Id,
            openVuln.Id,
            install.SoftwareProductId,
            install.Id,
            install.Version,
            ExposureMatchSource.Product,
            DateTimeOffset.UtcNow,
            runId: Guid.NewGuid());
        var resolvedExposure = DeviceVulnerabilityExposure.Observe(
            _tenantId,
            device.Id,
            resolvedVuln.Id,
            install.SoftwareProductId,
            install.Id,
            install.Version,
            ExposureMatchSource.Product,
            DateTimeOffset.UtcNow,
            runId: Guid.NewGuid());
        resolvedExposure.Resolve(DateTimeOffset.UtcNow);

        await _dbContext.AddRangeAsync(openExposure, resolvedExposure);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(
            new DeviceFilterQuery(),
            new PaginationQuery(),
            CancellationToken.None
        );

        var payload = action.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<PagedResponse<DeviceDto>>().Subject;
        payload.Items.Single().VulnerabilityCount.Should().Be(1);
    }

    [Fact]
    public async Task Get_ReturnsDeviceDetailWithCanonicalFields()
    {
        var label = BusinessLabel.Create(_tenantId, "Revenue", null, "#00ff00");
        var device = CreateDevice("device-1", "Device 1", Criticality.High);
        device.UpdateInventoryDetails(
            computerDnsName: "device-1.contoso.local",
            healthStatus: "Active",
            osPlatform: "Windows",
            osVersion: "11",
            externalRiskLabel: "High",
            lastSeenAt: DateTimeOffset.UtcNow,
            lastIpAddress: "10.0.0.1",
            aadDeviceId: "aad-1",
            groupId: "g1",
            groupName: "Workstations",
            exposureLevel: "Medium",
            isAadJoined: true,
            onboardingStatus: "Onboarded",
            deviceValue: "Normal"
        );

        await _dbContext.AddRangeAsync(
            label,
            device,
            DeviceBusinessLabel.Create(_tenantId, device.Id, label.Id),
            DeviceTag.Create(_tenantId, device.Id, "env", "prod"),
            DeviceRiskScore.Create(
                _tenantId,
                device.Id,
                812m,
                790m,
                1,
                1,
                0,
                0,
                2,
                "[]",
                RiskScoreService.CalculationVersion
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Get(device.Id, CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<DeviceDetailDto>().Subject;
        payload.Id.Should().Be(device.Id);
        payload.ExternalId.Should().Be("device-1");
        payload.ComputerDnsName.Should().Be("device-1.contoso.local");
        payload.OsPlatform.Should().Be("Windows");
        payload.GroupName.Should().Be("Workstations");
        payload.Tags.Should().ContainSingle().Which.Should().Be("prod");
        payload.BusinessLabels.Should().ContainSingle().Which.Name.Should().Be("Revenue");
        payload.Risk.Should().NotBeNull();
        payload.Risk!.OverallScore.Should().Be(812m);
        payload.Risk.MaxEpisodeRiskScore.Should().Be(790m);
        payload.Risk.RiskBand.Should().Be("High");
    }

    [Fact]
    public async Task Get_ReturnsNotFoundForUnknownDevice()
    {
        var action = await _controller.Get(Guid.NewGuid(), CancellationToken.None);
        action.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task ListExposures_ReturnsTenantScopedExposuresForDevice_And404ForCrossTenantDevice()
    {
        var tenantADevice = CreateDevice("device-a", "Device A", Criticality.High);
        var tenantB = Guid.NewGuid();
        var tenantBDevice = Device.Create(tenantB, _sourceSystemId, "device-b", "Device B", Criticality.Medium);
        var vuln = Vulnerability.Create("nvd", "CVE-2026-1000", "Exposure vuln", "desc", Severity.High, 7.5m, null, DateTimeOffset.UtcNow);

        var installA = InstalledSoftware.Observe(_tenantId, tenantADevice.Id, Guid.NewGuid(), _sourceSystemId, "1.0", DateTimeOffset.UtcNow);
        var installB = InstalledSoftware.Observe(tenantB, tenantBDevice.Id, Guid.NewGuid(), _sourceSystemId, "2.0", DateTimeOffset.UtcNow);

        await _dbContext.AddRangeAsync(tenantADevice, tenantBDevice, vuln, installA, installB);
        await _dbContext.SaveChangesAsync();

        var exposureA = DeviceVulnerabilityExposure.Observe(_tenantId, tenantADevice.Id, vuln.Id, installA.SoftwareProductId, installA.Id, installA.Version, ExposureMatchSource.Product, DateTimeOffset.UtcNow, Guid.NewGuid());
        var exposureB = DeviceVulnerabilityExposure.Observe(tenantB, tenantBDevice.Id, vuln.Id, installB.SoftwareProductId, installB.Id, installB.Version, ExposureMatchSource.Product, DateTimeOffset.UtcNow, Guid.NewGuid());
        await _dbContext.AddRangeAsync(exposureA, exposureB);
        await _dbContext.SaveChangesAsync();

        await _dbContext.ExposureAssessments.AddAsync(
            ExposureAssessment.Create(_tenantId, exposureA.Id, null, vuln.CvssScore ?? 0m, 8.2m, "test", DateTimeOffset.UtcNow));
        await _dbContext.SaveChangesAsync();

        var action = await _controller.ListExposures(tenantADevice.Id, new PaginationQuery(), CancellationToken.None);
        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<PagedResponse<DeviceExposureDto>>().Subject;
        payload.TotalCount.Should().Be(1);
        payload.Items.Should().ContainSingle();
        payload.Items[0].ExternalId.Should().Be("CVE-2026-1000");
        payload.Items[0].EnvironmentalCvss.Should().Be(8.2m);

        var crossTenant = await _controller.ListExposures(tenantBDevice.Id, new PaginationQuery(), CancellationToken.None);
        crossTenant.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetSoftware_ReturnsTenantSoftwareIdForDirectLinks()
    {
        var device = CreateDevice("device-1", "Device 1", Criticality.Medium);
        var product = SoftwareProduct.Create("Contoso", "Contoso Agent", null);
        var tenantSoftware = SoftwareTenantRecord.Create(
            _tenantId,
            null,
            product.Id,
            DateTimeOffset.UtcNow.AddDays(-5),
            DateTimeOffset.UtcNow.AddDays(-1)
        );
        var install = InstalledSoftware.Observe(
            _tenantId,
            device.Id,
            product.Id,
            _sourceSystemId,
            "1.0",
            DateTimeOffset.UtcNow
        );

        await _dbContext.AddRangeAsync(device, product, tenantSoftware, install);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetSoftware(device.Id, new PaginationQuery(), CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<PagedResponse<DeviceSoftwareItemDto>>().Subject;
        var item = payload.Items.Should().ContainSingle().Subject;
        item.TenantSoftwareId.Should().Be(tenantSoftware.Id);
        item.SoftwareProductId.Should().Be(product.Id);
        item.SoftwareName.Should().Be("Contoso Agent");
    }

    [Fact]
    public async Task AssignOwner_PersistsOwnerAndReturnsNoContent()
    {
        var device = CreateDevice("device-1", "Device 1", Criticality.Medium);
        await _dbContext.AddAsync(device);
        await _dbContext.SaveChangesAsync();

        var ownerId = Guid.NewGuid();
        var action = await _controller.AssignOwner(
            device.Id,
            new AssignDeviceOwnerRequest("User", ownerId),
            CancellationToken.None
        );

        action.Should().BeOfType<NoContentResult>();

        var stored = await _dbContext.Devices.SingleAsync(d => d.Id == device.Id);
        stored.OwnerType.Should().Be(OwnerType.User);
        stored.OwnerUserId.Should().Be(ownerId);
    }

    [Fact]
    public async Task AssignOwner_ReturnsNotFoundForUnknownDevice()
    {
        var action = await _controller.AssignOwner(
            Guid.NewGuid(),
            new AssignDeviceOwnerRequest("User", Guid.NewGuid()),
            CancellationToken.None
        );
        action.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task SetCriticality_AppliesManualOverride()
    {
        var device = CreateDevice("device-1", "Device 1", Criticality.Low);
        await _dbContext.AddAsync(device);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.SetCriticality(
            device.Id,
            new SetDeviceCriticalityRequest("Critical"),
            CancellationToken.None
        );

        action.Should().BeOfType<NoContentResult>();

        var stored = await _dbContext.Devices.SingleAsync(d => d.Id == device.Id);
        stored.Criticality.Should().Be(Criticality.Critical);
        stored.CriticalitySource.Should().Be("ManualOverride");
    }

    [Fact]
    public async Task ResetCriticalityOverride_ClearsManualOverride()
    {
        var device = CreateDevice("device-1", "Device 1", Criticality.Low);
        device.SetCriticality(Criticality.High);
        await _dbContext.AddAsync(device);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.ResetCriticalityOverride(
            device.Id,
            CancellationToken.None
        );

        action.Should().BeOfType<NoContentResult>();

        var stored = await _dbContext.Devices.SingleAsync(d => d.Id == device.Id);
        stored.Criticality.Should().Be(Criticality.Low);
        stored.CriticalitySource.Should().Be("Default");
    }

    [Fact]
    public async Task AssignBusinessLabels_ReplacesExistingLinks()
    {
        var label1 = BusinessLabel.Create(_tenantId, "Label 1", null, null);
        var label2 = BusinessLabel.Create(_tenantId, "Label 2", null, null);
        var label3 = BusinessLabel.Create(_tenantId, "Label 3", null, null);
        var device = CreateDevice("device-1", "Device 1", Criticality.Medium);

        await _dbContext.AddRangeAsync(
            label1,
            label2,
            label3,
            device,
            DeviceBusinessLabel.Create(_tenantId, device.Id, label1.Id)
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.AssignBusinessLabels(
            device.Id,
            new UpdateDeviceBusinessLabelsRequest([label2.Id, label3.Id]),
            CancellationToken.None
        );

        action.Should().BeOfType<NoContentResult>();

        var links = await _dbContext.DeviceBusinessLabels
            .Where(link => link.DeviceId == device.Id)
            .OrderBy(link => link.BusinessLabel.Name)
            .ToListAsync();
        links.Select(link => link.BusinessLabelId).Should().BeEquivalentTo(new[] { label2.Id, label3.Id });
    }

    [Fact]
    public async Task AssignBusinessLabels_RejectsUnknownLabels()
    {
        var device = CreateDevice("device-1", "Device 1", Criticality.Medium);
        await _dbContext.AddAsync(device);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.AssignBusinessLabels(
            device.Id,
            new UpdateDeviceBusinessLabelsRequest([Guid.NewGuid()]),
            CancellationToken.None
        );

        action.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task BulkAssign_UpdatesMultipleDevices()
    {
        var deviceA = CreateDevice("device-a", "Device A", Criticality.Medium);
        var deviceB = CreateDevice("device-b", "Device B", Criticality.Medium);
        await _dbContext.AddRangeAsync(deviceA, deviceB);
        await _dbContext.SaveChangesAsync();

        var teamId = Guid.NewGuid();
        var action = await _controller.BulkAssign(
            new BulkAssignDevicesRequest(
                new List<Guid> { deviceA.Id, deviceB.Id },
                "Team",
                teamId
            ),
            CancellationToken.None
        );

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<BulkAssignDevicesResponse>().Subject.UpdatedCount.Should().Be(2);

        var stored = await _dbContext.Devices
            .Where(d => d.Id == deviceA.Id || d.Id == deviceB.Id)
            .ToListAsync();
        stored.Should().OnlyContain(d => d.OwnerType == OwnerType.Team && d.OwnerTeamId == teamId);
    }

    private Device CreateDevice(string externalId, string name, Criticality criticality)
    {
        return Device.Create(_tenantId, _sourceSystemId, externalId, name, criticality);
    }

    // Phase 1 bridge: force Device.Id to a specific value so tests that rely on
    // legacy Asset-keyed tables can pair Asset+Device rows with matching ids.
    // Unused today (the DeviceDetailQueryService doesn't traverse Asset-keyed
    // tables) but kept available for forthcoming tests that will.
    private static void ForceId(Device device, Guid id)
    {
        typeof(Device)
            .GetProperty(
                nameof(Device.Id),
                BindingFlags.Public | BindingFlags.Instance
            )!
            .SetValue(device, id);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
