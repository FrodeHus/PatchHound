using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Decisions;
using PatchHound.Api.Services;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class RemediationDecisionsControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly IAiReportProvider _aiProvider;
    private readonly ITenantAiConfigurationResolver _aiConfigResolver;
    private readonly RemediationDecisionService _decisionService;
    private readonly RemediationWorkflowService _workflowService;
    private readonly RemediationDecisionsController _controller;

    public RemediationDecisionsControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.CurrentUserId.Returns(_userId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);
        _tenantContext.GetRolesForTenant(_tenantId).Returns(["GlobalAdmin"]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );

        _aiProvider = Substitute.For<IAiReportProvider>();
        _aiConfigResolver = Substitute.For<ITenantAiConfigurationResolver>();
        _workflowService = new RemediationWorkflowService(_dbContext);
        var notificationService = Substitute.For<INotificationService>();
        var patchingTaskService = new PatchingTaskService(_dbContext, new SlaService(), _workflowService, notificationService);
        var approvalTaskService = new ApprovalTaskService(
            _dbContext,
            notificationService,
            Substitute.For<IRealTimeNotifier>(),
            _workflowService,
            patchingTaskService
        );
        _decisionService = new RemediationDecisionService(
            _dbContext,
            approvalTaskService,
            _workflowService,
            patchingTaskService
        );
        var recommendationService = new AnalystRecommendationService(_dbContext, _workflowService);
        var queryService = new RemediationDecisionQueryService(
            _dbContext,
            new TenantSnapshotResolver(_dbContext),
            new SlaService(),
            new TenantAiTextGenerationService([_aiProvider], _aiConfigResolver),
            _tenantContext
        );
        var authorizationService = new RemediationWorkflowAuthorizationService(_dbContext, _tenantContext);

        _controller = new RemediationDecisionsController(
            queryService,
            _decisionService,
            approvalTaskService,
            recommendationService,
            authorizationService,
            _workflowService,
            new RemediationAiJobService(_dbContext),
            _dbContext,
            _tenantContext
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetDecisionContext_WhenAiProfileIsMissing_ReturnsSetupGuidance()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);

        var action = await _controller.GetDecisionContext(graph.TenantSoftware.Id, CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<DecisionContextDto>().Subject;
        payload.AiSummary.Content.Should().BeNull();
        payload.AiSummary.CanGenerate.Should().BeFalse();
        payload.AiSummary.UnavailableMessage.Should().Contain("Set up and enable a default AI profile");
    }

    [Fact]
    public async Task GenerateAiSummary_WhenEnabledProfileExists_QueuesBackgroundGeneration()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var profile = TenantAiProfileFactory.Create(
            _tenantId,
            providerType: TenantAiProviderType.Ollama,
            name: "Default AI",
            model: "llama3",
            systemPrompt: "system"
        );
        var deviceOne = await _dbContext.Assets.IgnoreQueryFilters()
            .FirstAsync(item => item.TenantId == _tenantId && item.ExternalId == "device-1");
        deviceOne.UpdateDeviceDetails(
            deviceOne.DeviceComputerDnsName,
            deviceOne.DeviceHealthStatus,
            deviceOne.DeviceOsPlatform,
            deviceOne.DeviceOsVersion,
            deviceOne.DeviceRiskScore,
            deviceOne.DeviceLastSeenAt,
            deviceOne.DeviceLastIpAddress,
            deviceOne.DeviceAadDeviceId,
            deviceOne.DeviceGroupId,
            deviceOne.DeviceGroupName,
            deviceOne.DeviceExposureLevel,
            deviceOne.DeviceIsAadJoined,
            deviceOne.DeviceOnboardingStatus,
            "Tier 0"
        );
        await _dbContext.TenantAiProfiles.AddAsync(profile);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GenerateAiSummary(graph.TenantSoftware.Id, CancellationToken.None);

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<DecisionAiSummaryDto>().Subject;
        payload.Content.Should().BeNull();
        payload.CanGenerate.Should().BeTrue();
        payload.IsGenerating.Should().BeTrue();

        var queuedJob = await _dbContext.RemediationAiJobs
            .IgnoreQueryFilters()
            .Where(item => item.TenantSoftwareId == graph.TenantSoftware.Id)
            .OrderByDescending(item => item.RequestedAt)
            .FirstOrDefaultAsync();
        queuedJob.Should().NotBeNull();
        queuedJob!.Status.Should().Be(RemediationAiJobStatus.Pending);
    }

    [Fact]
    public async Task ReviewAiSummary_WhenRemediationExists_PersistsReviewStatus()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var tenantSoftware = await _dbContext.TenantSoftware
            .FirstAsync(item => item.TenantId == _tenantId && item.Id == graph.TenantSoftware.Id);
        tenantSoftware.StoreRemediationAiSummary(
            "Executive summary",
            "Owner recommendation",
            "Analyst assessment",
            "Exception recommendation",
            nameof(RemediationOutcome.ApprovedForPatching),
            "High",
            "hash",
            "OpenAI",
            "Default AI",
            "gpt-test"
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.ReviewAiSummary(
            graph.TenantSoftware.Id,
            new ReviewDecisionAiSummaryRequest("accept"),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<DecisionAiSummaryDto>().Subject;
        payload.ReviewStatus.Should().Be("Accepted");
        payload.ReviewedAt.Should().NotBeNull();
        payload.ReviewedByDisplayName.Should().BeNull();

        var persisted = await _dbContext.TenantSoftware
            .IgnoreQueryFilters()
            .FirstAsync(item => item.TenantId == _tenantId && item.Id == graph.TenantSoftware.Id);
        persisted.RemediationAiReviewStatus.Should().Be("Accepted");
        persisted.RemediationAiReviewedBy.Should().Be(_userId);
        persisted.RemediationAiReviewedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyRecurringRemediation_KeepCurrentDecision_ReopensRiskAcceptanceIntoApproval()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var team = Team.Create(_tenantId, "Infrastructure");
        await _dbContext.Teams.AddAsync(team);

        var devices = await _dbContext.Assets
            .Where(item => item.AssetType == AssetType.Device)
            .ToListAsync();
        foreach (var device in devices)
        {
            device.AssignTeamOwner(team.Id);
        }

        await _dbContext.SaveChangesAsync();

        var softwareAssetId = await _dbContext.NormalizedSoftwareInstallations
            .Where(item => item.TenantSoftwareId == graph.TenantSoftware.Id)
            .Select(item => item.SoftwareAssetId)
            .OrderBy(id => id)
            .FirstAsync();

        var firstDecisionResult = await _decisionService.CreateDecisionAsync(
            _tenantId,
            softwareAssetId,
            RemediationOutcome.RiskAcceptance,
            "Existing exception.",
            _userId,
            DateTimeOffset.UtcNow.AddDays(30),
            null,
            CancellationToken.None
        );
        firstDecisionResult.IsSuccess.Should().BeTrue();
        var approvalTaskService = new ApprovalTaskService(
            _dbContext,
            Substitute.For<INotificationService>(),
            Substitute.For<IRealTimeNotifier>(),
            _workflowService,
            new PatchingTaskService(_dbContext, new SlaService(), _workflowService, Substitute.For<INotificationService>())
        );
        var firstApprovalTask = await _dbContext.ApprovalTasks
            .OrderByDescending(item => item.CreatedAt)
            .FirstAsync();
        await approvalTaskService.ApproveAsync(firstApprovalTask.Id, _userId, "Exception renewed.", CancellationToken.None);
        await _dbContext.SaveChangesAsync();

        var firstWorkflow = await _dbContext.RemediationWorkflows
            .OrderByDescending(item => item.CreatedAt)
            .FirstAsync();
        firstWorkflow.Complete();
        await _dbContext.SaveChangesAsync();

        var recurringWorkflow = await _workflowService.GetOrCreateActiveWorkflowAsync(
            _tenantId,
            graph.TenantSoftware.Id,
            CancellationToken.None
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.VerifyRecurringRemediation(
            recurringWorkflow.Id,
            new VerifyRemediationRequest("keepCurrentDecision"),
            CancellationToken.None
        );

        action.Should().BeOfType<OkResult>();

        var updatedWorkflow = await _dbContext.RemediationWorkflows
            .FirstAsync(item => item.Id == recurringWorkflow.Id);
        updatedWorkflow.CurrentStage.Should().Be(RemediationWorkflowStage.Approval);
    }

    [Fact]
    public async Task VerifyRecurringRemediation_ChooseNewDecision_ReopensDecisionStage()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var team = Team.Create(_tenantId, "Infrastructure");
        await _dbContext.Teams.AddAsync(team);

        var devices = await _dbContext.Assets
            .Where(item => item.AssetType == AssetType.Device)
            .ToListAsync();
        foreach (var device in devices)
        {
            device.AssignTeamOwner(team.Id);
        }

        await _dbContext.SaveChangesAsync();

        var softwareAssetId = await _dbContext.NormalizedSoftwareInstallations
            .Where(item => item.TenantSoftwareId == graph.TenantSoftware.Id)
            .Select(item => item.SoftwareAssetId)
            .OrderBy(id => id)
            .FirstAsync();

        var firstDecisionResult = await _decisionService.CreateDecisionAsync(
            _tenantId,
            softwareAssetId,
            RemediationOutcome.ApprovedForPatching,
            "Patch previously.",
            _userId,
            null,
            null,
            CancellationToken.None
        );
        firstDecisionResult.IsSuccess.Should().BeTrue();
        var approvalTaskService = new ApprovalTaskService(
            _dbContext,
            Substitute.For<INotificationService>(),
            Substitute.For<IRealTimeNotifier>(),
            _workflowService,
            new PatchingTaskService(_dbContext, new SlaService(), _workflowService, Substitute.For<INotificationService>())
        );
        var firstApprovalTask = await _dbContext.ApprovalTasks
            .OrderByDescending(item => item.CreatedAt)
            .FirstAsync();
        await approvalTaskService.ApproveAsync(firstApprovalTask.Id, _userId, "Patch confirmed.", CancellationToken.None);

        var firstWorkflow = await _dbContext.RemediationWorkflows
            .OrderByDescending(item => item.CreatedAt)
            .FirstAsync();
        firstWorkflow.Complete();
        await _dbContext.SaveChangesAsync();

        var recurringWorkflow = await _workflowService.GetOrCreateActiveWorkflowAsync(
            _tenantId,
            graph.TenantSoftware.Id,
            CancellationToken.None
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.VerifyRecurringRemediation(
            recurringWorkflow.Id,
            new VerifyRemediationRequest("chooseNewDecision"),
            CancellationToken.None
        );

        action.Should().BeOfType<OkResult>();

        var updatedWorkflow = await _dbContext.RemediationWorkflows
            .FirstAsync(item => item.Id == recurringWorkflow.Id);
        updatedWorkflow.CurrentStage.Should().Be(RemediationWorkflowStage.RemediationDecision);
    }

    [Fact]
    public async Task GetDecisionContext_WhenApprovalWasDenied_ReturnsRejectedDecisionWithRejectionComment()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var team = Team.Create(_tenantId, "Infrastructure");
        await _dbContext.Teams.AddAsync(team);

        var devices = await _dbContext.Assets
            .Where(item => item.AssetType == AssetType.Device)
            .ToListAsync();
        foreach (var device in devices)
        {
            device.AssignTeamOwner(team.Id);
        }

        await _dbContext.SaveChangesAsync();

        var softwareAssetId = await _dbContext.NormalizedSoftwareInstallations
            .Where(item => item.TenantSoftwareId == graph.TenantSoftware.Id)
            .Select(item => item.SoftwareAssetId)
            .OrderBy(id => id)
            .FirstAsync();

        var decisionResult = await _decisionService.CreateDecisionAsync(
            _tenantId,
            softwareAssetId,
            RemediationOutcome.RiskAcceptance,
            "Accept the current risk for now.",
            _userId,
            DateTimeOffset.UtcNow.AddDays(14),
            null,
            CancellationToken.None
        );
        decisionResult.IsSuccess.Should().BeTrue();

        var approvalTaskService = new ApprovalTaskService(
            _dbContext,
            Substitute.For<INotificationService>(),
            Substitute.For<IRealTimeNotifier>(),
            _workflowService,
            new PatchingTaskService(_dbContext, new SlaService(), _workflowService, Substitute.For<INotificationService>())
        );

        var approvalTask = await _dbContext.ApprovalTasks
            .OrderByDescending(item => item.CreatedAt)
            .FirstAsync();
        await approvalTaskService.DenyAsync(approvalTask.Id, _userId, "Risk too high without compensating controls.", CancellationToken.None);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetDecisionContext(graph.TenantSoftware.Id, CancellationToken.None);
        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<DecisionContextDto>().Subject;

        payload.CurrentDecision.Should().NotBeNull();
        payload.CurrentDecision!.ApprovalStatus.Should().Be("Rejected");
        payload.CurrentDecision.LatestRejection.Should().NotBeNull();
        payload.CurrentDecision.LatestRejection!.Comment.Should().Be("Risk too high without compensating controls.");
    }

    [Fact]
    public async Task DecisionList_ExcludesSoftwareWithZeroOpenVulnerabilities()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var aiConfigResolver = Substitute.For<ITenantAiConfigurationResolver>();
        var queryService = new RemediationDecisionQueryService(
            _dbContext,
            new TenantSnapshotResolver(_dbContext),
            new SlaService(),
            new TenantAiTextGenerationService(Array.Empty<IAiReportProvider>(), aiConfigResolver),
            _tenantContext
        );
        var listController = new DecisionListController(queryService, _tenantContext);

        var projections = await _dbContext.NormalizedSoftwareVulnerabilityProjections
            .Where(item => item.TenantSoftwareId == graph.TenantSoftware.Id)
            .ToListAsync();
        _dbContext.NormalizedSoftwareVulnerabilityProjections.RemoveRange(projections);

        var matches = await _dbContext.SoftwareVulnerabilityMatches
            .Where(item => item.TenantId == _tenantId)
            .ToListAsync();
        foreach (var match in matches)
        {
            match.Resolve(DateTimeOffset.UtcNow);
        }

        await _dbContext.SaveChangesAsync();

        var action = await listController.List(
            new RemediationDecisionFilterQuery(),
            new PaginationQuery(1, 20),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<RemediationDecisionListPageDto>().Subject;
        payload.TotalCount.Should().Be(0);
        payload.Items.Should().BeEmpty();
    }
}
