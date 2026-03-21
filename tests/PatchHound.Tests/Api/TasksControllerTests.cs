using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Tasks;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Repositories;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class TasksControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly TasksController _controller;

    public TasksControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);
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
            snapshotResolver,
            new VulnerabilityAssessmentService(
                _dbContext,
                new EnvironmentalSeverityCalculator(),
                snapshotResolver
            ),
            new VulnerabilityEpisodeRiskAssessmentService(_dbContext),
            new RiskScoreService(_dbContext, Substitute.For<Microsoft.Extensions.Logging.ILogger<RiskScoreService>>())
        );

        _controller = new TasksController(
            _dbContext,
            new RemediationTaskService(
                new RemediationTaskRepository(_dbContext),
                _dbContext
            ),
            new RiskAcceptanceService(
                new RiskAcceptanceRepository(_dbContext),
                _dbContext
            ),
            riskRefreshService,
            _tenantContext
        );
    }

    [Fact]
    public async Task List_OrdersTasksByEpisodeRiskBeforeDueDate()
    {
        var vulnerabilityA = VulnerabilityDefinition.Create(
            "CVE-2026-8100",
            "Higher risk",
            "Desc",
            Severity.High,
            "NVD"
        );
        var vulnerabilityB = VulnerabilityDefinition.Create(
            "CVE-2026-8101",
            "Lower risk",
            "Desc",
            Severity.Medium,
            "NVD"
        );
        var tenantVulnerabilityA = TenantVulnerability.Create(
            _tenantId,
            vulnerabilityA.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow,
            "MicrosoftDefender"
        );
        var tenantVulnerabilityB = TenantVulnerability.Create(
            _tenantId,
            vulnerabilityB.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow,
            "MicrosoftDefender"
        );
        var assetA = Asset.Create(_tenantId, "asset-a", AssetType.Device, "Asset A", Criticality.High);
        var assetB = Asset.Create(_tenantId, "asset-b", AssetType.Device, "Asset B", Criticality.High);
        var taskLowRisk = RemediationTask.Create(
            tenantVulnerabilityB.Id,
            assetB.Id,
            _tenantId,
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow.AddDays(-1)
        );
        var taskHighRisk = RemediationTask.Create(
            tenantVulnerabilityA.Id,
            assetA.Id,
            _tenantId,
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow.AddDays(5)
        );

        await _dbContext.AddRangeAsync(
            vulnerabilityA,
            vulnerabilityB,
            tenantVulnerabilityA,
            tenantVulnerabilityB,
            assetA,
            assetB,
            taskLowRisk,
            taskHighRisk,
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                Guid.NewGuid(),
                tenantVulnerabilityA.Id,
                assetA.Id,
                null,
                90m,
                80m,
                50m,
                880m,
                "High",
                "[]",
                VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            ),
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                Guid.NewGuid(),
                tenantVulnerabilityB.Id,
                assetB.Id,
                null,
                45m,
                40m,
                35m,
                420m,
                "Low",
                "[]",
                VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(
            new TaskFilterQuery(),
            new PaginationQuery(),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = result.Value.Should().BeOfType<PagedResponse<RemediationTaskDto>>().Subject;

        payload.Items.Should().HaveCount(2);
        payload.Items[0].VulnerabilityTitle.Should().Be("Higher risk");
        payload.Items[0].EpisodeRiskScore.Should().Be(880m);
        payload.Items[0].EpisodeRiskBand.Should().Be("High");
        payload.Items[1].VulnerabilityTitle.Should().Be("Lower risk");
    }

    [Fact]
    public async Task UpdateStatus_RecalculatesEpisodeRiskAndTenantRollups()
    {
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-8200",
            "Task-driven risk change",
            "Desc",
            Severity.High,
            "NVD",
            8.0m
        );
        var tenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            definition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow,
            "MicrosoftDefender"
        );
        var asset = Asset.Create(_tenantId, "asset-risk", AssetType.Device, "Asset Risk", Criticality.High);
        var episode = VulnerabilityAssetEpisode.Create(
            _tenantId,
            tenantVulnerability.Id,
            asset.Id,
            1,
            DateTimeOffset.UtcNow.AddDays(-2)
        );
        var task = RemediationTask.Create(
            tenantVulnerability.Id,
            asset.Id,
            _tenantId,
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow.AddDays(5)
        );

        await _dbContext.AddRangeAsync(definition, tenantVulnerability, asset, episode, task);
        await _dbContext.VulnerabilityEpisodeRiskAssessments.AddAsync(
            VulnerabilityEpisodeRiskAssessment.Create(
                _tenantId,
                episode.Id,
                tenantVulnerability.Id,
                asset.Id,
                null,
                80m,
                70m,
                60m,
                735m,
                "Medium",
                "[]",
                VulnerabilityEpisodeRiskAssessmentService.CalculationVersion
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.UpdateStatus(
            task.Id,
            new UpdateTaskStatusRequest(nameof(RemediationTaskStatus.InProgress), null),
            CancellationToken.None
        );

        action.Should().BeOfType<NoContentResult>();

        var refreshedAssessment = await _dbContext.VulnerabilityEpisodeRiskAssessments
            .SingleAsync(item => item.VulnerabilityAssetEpisodeId == episode.Id);
        refreshedAssessment.OperationalScore.Should().Be(60m);
        refreshedAssessment.EpisodeRiskScore.Should().BeLessThan(735m);

        var assetRisk = await _dbContext.AssetRiskScores.SingleAsync(item => item.AssetId == asset.Id);
        assetRisk.OverallScore.Should().BeLessThan(735m);
        assetRisk.OpenEpisodeCount.Should().Be(1);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
