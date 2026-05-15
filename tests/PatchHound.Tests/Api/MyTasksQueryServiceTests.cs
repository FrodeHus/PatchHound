using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Models.MyTasks;
using PatchHound.Api.Services;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class MyTasksQueryServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;
    private readonly MyTasksQueryService _service;
    private int _cveCounter = 1000;

    public MyTasksQueryServiceTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.CurrentUserId.Returns(_userId);
        tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(tenantContext)
        );
        _service = new MyTasksQueryService(_dbContext, new SlaService());
    }

    [Fact]
    public async Task ListAsync_ReturnsRoleRelevantBucketsOnly()
    {
        await SeedCaseAsync("Needs Recommendation", workflowStage: null, decisionOutcome: null);
        await SeedCaseAsync("Needs Decision", RemediationWorkflowStage.RemediationDecision, decisionOutcome: null);

        var result = await _service.ListAsync(
            _tenantId,
            [RoleName.SecurityAnalyst.ToString()],
            new MyTasksQuery(),
            CancellationToken.None
        );

        result.Sections.Should().ContainSingle();
        result.Sections.Single().Bucket.Should().Be(MyTaskBuckets.Recommendation);
        result.Sections.Single().Items.Should().ContainSingle(item => item.SoftwareName == "Needs Recommendation");
    }

    [Fact]
    public async Task ListAsync_RoutesApprovalBucketByManagerRoleAndOutcome()
    {
        await SeedCaseAsync(
            "Accepted Risk",
            RemediationWorkflowStage.Approval,
            RemediationOutcome.RiskAcceptance
        );
        await SeedCaseAsync(
            "Patch Approval",
            RemediationWorkflowStage.Approval,
            RemediationOutcome.ApprovedForPatching
        );

        var securityResult = await _service.ListAsync(
            _tenantId,
            [RoleName.SecurityManager.ToString()],
            new MyTasksQuery(),
            CancellationToken.None
        );
        var technicalResult = await _service.ListAsync(
            _tenantId,
            [RoleName.TechnicalManager.ToString()],
            new MyTasksQuery(),
            CancellationToken.None
        );

        securityResult.Sections.Single(section => section.Bucket == MyTaskBuckets.Approval)
            .Items.Should()
            .ContainSingle(item => item.SoftwareName == "Accepted Risk");
        technicalResult.Sections.Single(section => section.Bucket == MyTaskBuckets.Approval)
            .Items.Should()
            .ContainSingle(item => item.SoftwareName == "Patch Approval");
    }

    [Fact]
    public async Task ListAsync_UsesPageSizePlusOneForHasMore()
    {
        await SeedCaseAsync("Recommendation 1", workflowStage: null, decisionOutcome: null);
        await SeedCaseAsync("Recommendation 2", workflowStage: null, decisionOutcome: null);
        await SeedCaseAsync("Recommendation 3", workflowStage: null, decisionOutcome: null);

        var result = await _service.ListAsync(
            _tenantId,
            [RoleName.SecurityAnalyst.ToString()],
            new MyTasksQuery(PageSize: 2),
            CancellationToken.None
        );

        var section = result.Sections.Should().ContainSingle().Subject;
        section.Items.Should().HaveCount(2);
        section.HasMore.Should().BeTrue();
        section.PageSize.Should().Be(2);
    }

    private async Task<RemediationCase> SeedCaseAsync(
        string productName,
        RemediationWorkflowStage? workflowStage,
        RemediationOutcome? decisionOutcome
    )
    {
        var ownerTeam = Team.Create(_tenantId, $"{productName} Team");
        var product = SoftwareProduct.Create("Contoso", productName, null);
        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        var device = CanonicalTestData.MakeDevice(_tenantId);
        var installedSoftware = CanonicalTestData.MakeInstalledSoftware(_tenantId, device.Id, product.Id);
        var vulnerability = Vulnerability.Create(
            "nvd",
            $"CVE-2026-{_cveCounter++}",
            $"{productName} vulnerability",
            "Test vulnerability.",
            Severity.Critical,
            9.8m,
            null,
            DateTimeOffset.UtcNow.AddDays(-7)
        );
        var exposure = DeviceVulnerabilityExposure.Observe(
            _tenantId,
            device.Id,
            vulnerability.Id,
            product.Id,
            installedSoftware.Id,
            installedSoftware.Version,
            ExposureMatchSource.Product,
            DateTimeOffset.UtcNow.AddDays(-2)
        );
        var riskScore = SoftwareRiskScore.Create(
            _tenantId,
            product.Id,
            overallScore: 90m,
            maxExposureScore: 90m,
            criticalExposureCount: 1,
            highExposureCount: 0,
            mediumExposureCount: 0,
            lowExposureCount: 0,
            affectedDeviceCount: 1,
            openExposureCount: 1,
            factorsJson: "[]",
            calculationVersion: "test"
        );

        var entities = new List<object>
        {
            ownerTeam,
            product,
            remediationCase,
            device,
            installedSoftware,
            vulnerability,
            exposure,
            riskScore,
        };

        if (workflowStage is not null || decisionOutcome is not null)
        {
            var workflow = RemediationWorkflow.Create(_tenantId, remediationCase.Id, ownerTeam.Id);
            if (workflowStage is RemediationWorkflowStage.RemediationDecision or RemediationWorkflowStage.Approval)
            {
                workflow.MoveToStage(RemediationWorkflowStage.RemediationDecision);
            }

            if (workflowStage is RemediationWorkflowStage.Approval)
            {
                workflow.MoveToStage(RemediationWorkflowStage.Approval);
            }

            entities.Add(workflow);

            if (decisionOutcome is RemediationOutcome outcome)
            {
                entities.Add(RemediationDecision.Create(
                    _tenantId,
                    remediationCase.Id,
                    outcome,
                    "Pending approval",
                    _userId,
                    DecisionApprovalStatus.PendingApproval
                ));
            }
        }

        await _dbContext.AddRangeAsync(entities);
        await _dbContext.SaveChangesAsync();
        return remediationCase;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
