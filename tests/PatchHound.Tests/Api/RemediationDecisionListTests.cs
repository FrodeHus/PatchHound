using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Models;
using PatchHound.Api.Services;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Common;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class RemediationDecisionListTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly RemediationDecisionQueryService _service;

    public RemediationDecisionListTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.CurrentUserId.Returns(_userId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );

        var aiConfigurationResolver = Substitute.For<ITenantAiConfigurationResolver>();
        aiConfigurationResolver
            .ResolveDefaultAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result<TenantAiProfileResolved>.Failure("No AI profile configured."));
        _service = new RemediationDecisionQueryService(
            _dbContext,
            new SlaService(),
            aiConfigurationResolver,
            _tenantContext
        );
    }

    [Fact]
    public async Task BuildByCaseIdAsync_ReturnsTenantSoftwareIdAndLatestApprovalResolution()
    {
        var approver = User.Create("approver@example.test", "Adele Approver", Guid.NewGuid().ToString());
        var product = SoftwareProduct.Create("Contoso", "Contoso Agent", null);
        var tenantSoftware = SoftwareTenantRecord.Create(
            _tenantId,
            null,
            product.Id,
            DateTimeOffset.UtcNow.AddDays(-5),
            DateTimeOffset.UtcNow.AddDays(-1)
        );
        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        var decision = RemediationDecision.Create(
            _tenantId,
            remediationCase.Id,
            RemediationOutcome.RiskAcceptance,
            "Owner rationale from the database",
            _userId,
            DecisionApprovalStatus.PendingApproval
        );
        var task = ApprovalTask.Create(
            _tenantId,
            remediationCase.Id,
            decision.Id,
            RemediationOutcome.RiskAcceptance,
            ApprovalTaskStatus.Pending,
            DateTimeOffset.UtcNow.AddDays(1)
        );
        task.Approve(approver.Id, "Approver rationale from the database");
        decision.Approve(approver.Id);

        await _dbContext.AddRangeAsync(approver, product, tenantSoftware, remediationCase, decision, task);
        await _dbContext.SaveChangesAsync();

        var result = await _service.BuildByCaseIdAsync(
            _tenantId,
            remediationCase.Id,
            CancellationToken.None
        );

        result.Should().NotBeNull();
        result!.TenantSoftwareId.Should().Be(tenantSoftware.Id);
        result.CurrentDecision!.Justification.Should().Be("Owner rationale from the database");
        result.LatestApprovalResolution.Should().NotBeNull();
        result.LatestApprovalResolution!.Justification.Should().Be("Approver rationale from the database");
        result.LatestApprovalResolution.ResolvedByDisplayName.Should().Be("Adele Approver");
    }

    [Fact]
    public async Task BuildByCaseIdAsync_ReturnsAnalystWorkbenchMetadata()
    {
        var sourceSystemId = Guid.NewGuid();
        var product = SoftwareProduct.Create("Contoso", "Contoso Agent", null);
        product.UpdateIdentity("Endpoint agent", null, SoftwareNormalizationMethod.Heuristic, SoftwareNormalizationConfidence.High, DateTimeOffset.UtcNow);
        var insight = TenantSoftwareProductInsight.Create(_tenantId, product.Id);
        insight.UpdateDescription("Tenant-specific description for analysts.");
        var tenantSoftware = SoftwareTenantRecord.Create(
            _tenantId,
            null,
            product.Id,
            DateTimeOffset.UtcNow.AddDays(-10),
            DateTimeOffset.UtcNow.AddDays(-1)
        );
        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        var device = Device.Create(_tenantId, sourceSystemId, "device-1", "Device 1", Criticality.High);
        var label = BusinessLabel.Create(_tenantId, "Revenue", null, "#22c55e", BusinessLabelWeightCategory.Critical);
        var installedSoftware = InstalledSoftware.Observe(
            _tenantId,
            device.Id,
            product.Id,
            sourceSystemId,
            "1.2.3",
            DateTimeOffset.UtcNow.AddDays(-2)
        );
        var vulnerability = Vulnerability.Create(
            "nvd",
            "CVE-2026-4242",
            "Remote code execution",
            "A remotely exploitable vulnerability.",
            Severity.Critical,
            9.8m,
            "CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H",
            DateTimeOffset.UtcNow.AddDays(-30)
        );
        var exposure = DeviceVulnerabilityExposure.Observe(
            _tenantId,
            device.Id,
            vulnerability.Id,
            product.Id,
            installedSoftware.Id,
            "1.2.3",
            ExposureMatchSource.Product,
            DateTimeOffset.UtcNow.AddDays(-2)
        );
        var threat = ThreatAssessment.Create(
            vulnerability.Id,
            threatScore: 95m,
            technicalScore: 98m,
            exploitLikelihoodScore: 90m,
            threatActivityScore: 90m,
            epssScore: 0.42m,
            knownExploited: true,
            publicExploit: true,
            activeAlert: false,
            hasRansomwareAssociation: false,
            hasMalwareAssociation: false,
            factorsJson: "[]",
            calculationVersion: "test"
        );

        await _dbContext.AddRangeAsync(
            product,
            insight,
            tenantSoftware,
            remediationCase,
            device,
            label,
            DeviceBusinessLabel.Create(_tenantId, device.Id, label.Id),
            installedSoftware,
            vulnerability,
            exposure,
            threat
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.BuildByCaseIdAsync(
            _tenantId,
            remediationCase.Id,
            CancellationToken.None
        );

        result.Should().NotBeNull();
        result!.SoftwareVendor.Should().Be("Contoso");
        result.SoftwareCategory.Should().Be("Endpoint agent");
        result.SoftwareDescription.Should().Be("Tenant-specific description for analysts.");
        result.BusinessLabels.Should().ContainSingle().Which.Name.Should().Be("Revenue");
        result.BusinessLabels.Single().AffectedDeviceCount.Should().Be(1);
        result.BusinessLabels.Single().WeightCategory.Should().Be(nameof(BusinessLabelWeightCategory.Critical));
        result.OpenVulnerabilities.Should().ContainSingle();
        var vuln = result.OpenVulnerabilities.Single();
        vuln.ExternalId.Should().Be("CVE-2026-4242");
        vuln.Description.Should().Be("A remotely exploitable vulnerability.");
        vuln.FirstSeenAt.Should().NotBeNull();
        vuln.AffectedDeviceCount.Should().Be(1);
        vuln.AffectedVersionCount.Should().Be(1);
        vuln.KnownExploited.Should().BeTrue();
        vuln.PublicExploit.Should().BeTrue();
        vuln.EpssScore.Should().Be(0.42);
    }

    [Fact]
    public async Task ListAsync_ReturnsSoftwareOwnerRoutingFields()
    {
        var ownerTeam = Team.Create(_tenantId, "Platform Engineering");
        var product = SoftwareProduct.Create("Contoso", "Contoso Agent", null);
        var tenantSoftware = SoftwareTenantRecord.Create(
            _tenantId,
            null,
            product.Id,
            DateTimeOffset.UtcNow.AddDays(-5),
            DateTimeOffset.UtcNow.AddDays(-1)
        );
        tenantSoftware.AssignOwnerTeamFromRule(ownerTeam.Id, Guid.NewGuid());

        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        var workflow = RemediationWorkflow.Create(_tenantId, remediationCase.Id, ownerTeam.Id);
        var device = CanonicalTestData.MakeDevice(_tenantId);
        var installedSoftware = CanonicalTestData.MakeInstalledSoftware(_tenantId, device.Id, product.Id);

        await _dbContext.AddRangeAsync(ownerTeam, product, tenantSoftware, remediationCase, workflow, device, installedSoftware);
        await _dbContext.SaveChangesAsync();

        var result = await _service.ListAsync(
            _tenantId,
            new PatchHound.Api.Models.Decisions.RemediationDecisionFilterQuery(),
            new PaginationQuery(),
            CancellationToken.None
        );

        var item = result.Items.Should().ContainSingle().Subject;
        item.SoftwareOwnerTeamName.Should().Be("Platform Engineering");
        item.SoftwareOwnerAssignmentSource.Should().Be("Rule");
    }

    [Fact]
    public async Task ListAsync_WhenNeedsAnalystRecommendation_ReturnsSecurityAnalysisCasesWithoutRecommendations()
    {
        var needsRecommendationProduct = SoftwareProduct.Create("Contoso", "Needs Recommendation", null);
        var needsRecommendationCase = RemediationCase.Create(_tenantId, needsRecommendationProduct.Id);
        var needsRecommendationWorkflow = RemediationWorkflow.Create(_tenantId, needsRecommendationCase.Id, Guid.NewGuid());
        var needsRecommendationDevice = CanonicalTestData.MakeDevice(_tenantId);
        var needsRecommendationSoftware = CanonicalTestData.MakeInstalledSoftware(_tenantId, needsRecommendationDevice.Id, needsRecommendationProduct.Id);

        var alreadyRecommendedProduct = SoftwareProduct.Create("Contoso", "Already Recommended", null);
        var alreadyRecommendedCase = RemediationCase.Create(_tenantId, alreadyRecommendedProduct.Id);
        var alreadyRecommendedWorkflow = RemediationWorkflow.Create(_tenantId, alreadyRecommendedCase.Id, Guid.NewGuid());
        var recommendation = AnalystRecommendation.Create(
            _tenantId,
            alreadyRecommendedCase.Id,
            RemediationOutcome.ApprovedForPatching,
            "Patch this software.",
            _userId
        );
        recommendation.AttachToWorkflow(alreadyRecommendedWorkflow.Id);
        var alreadyRecommendedDevice = CanonicalTestData.MakeDevice(_tenantId);
        var alreadyRecommendedSoftware = CanonicalTestData.MakeInstalledSoftware(_tenantId, alreadyRecommendedDevice.Id, alreadyRecommendedProduct.Id);

        var decisionStageProduct = SoftwareProduct.Create("Contoso", "Decision Stage", null);
        var decisionStageCase = RemediationCase.Create(_tenantId, decisionStageProduct.Id);
        var decisionStageWorkflow = RemediationWorkflow.Create(_tenantId, decisionStageCase.Id, Guid.NewGuid());
        decisionStageWorkflow.MoveToStage(RemediationWorkflowStage.RemediationDecision);
        var decisionStageDevice = CanonicalTestData.MakeDevice(_tenantId);
        var decisionStageSoftware = CanonicalTestData.MakeInstalledSoftware(_tenantId, decisionStageDevice.Id, decisionStageProduct.Id);

        await _dbContext.AddRangeAsync(
            needsRecommendationProduct,
            needsRecommendationCase,
            needsRecommendationWorkflow,
            needsRecommendationDevice,
            needsRecommendationSoftware,
            alreadyRecommendedProduct,
            alreadyRecommendedCase,
            alreadyRecommendedWorkflow,
            recommendation,
            alreadyRecommendedDevice,
            alreadyRecommendedSoftware,
            decisionStageProduct,
            decisionStageCase,
            decisionStageWorkflow,
            decisionStageDevice,
            decisionStageSoftware
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.ListAsync(
            _tenantId,
            new PatchHound.Api.Models.Decisions.RemediationDecisionFilterQuery(NeedsAnalystRecommendation: true),
            new PaginationQuery(),
            CancellationToken.None
        );

        var item = result.Items.Should().ContainSingle().Subject;
        item.RemediationCaseId.Should().Be(needsRecommendationCase.Id);
        item.WorkflowStage.Should().Be(nameof(RemediationWorkflowStage.SecurityAnalysis));
    }

    [Fact]
    public async Task ListAsync_WhenNeedsAnalystRecommendation_IncludesCasesWithoutAnyWorkflow()
    {
        // Workflows are bootstrapped lazily when an analyst saves a recommendation
        // or an owner saves a decision. Brand-new cases that nobody has touched yet
        // should still appear in the analyst recommendation queue.
        var product = SoftwareProduct.Create("Contoso", "Untouched Software", null);
        var untouchedCase = RemediationCase.Create(_tenantId, product.Id);
        var device = CanonicalTestData.MakeDevice(_tenantId);
        var installedSoftware = CanonicalTestData.MakeInstalledSoftware(_tenantId, device.Id, product.Id);
        await _dbContext.AddRangeAsync(product, untouchedCase, device, installedSoftware);
        await _dbContext.SaveChangesAsync();

        var result = await _service.ListAsync(
            _tenantId,
            new PatchHound.Api.Models.Decisions.RemediationDecisionFilterQuery(NeedsAnalystRecommendation: true),
            new PaginationQuery(),
            CancellationToken.None
        );

        var item = result.Items.Should().ContainSingle().Subject;
        item.RemediationCaseId.Should().Be(untouchedCase.Id);
        // No workflow exists yet → WorkflowStage is null in the projection.
        item.WorkflowStage.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_WhenNeedsRemediationDecision_ReturnsRemediationDecisionStageCasesWithoutDecision()
    {
        // Case at RemediationDecision stage with no decision → matches.
        var pendingProduct = SoftwareProduct.Create("Contoso", "Awaiting Decision", null);
        var pendingCase = RemediationCase.Create(_tenantId, pendingProduct.Id);
        var pendingWorkflow = RemediationWorkflow.Create(_tenantId, pendingCase.Id, Guid.NewGuid());
        pendingWorkflow.MoveToStage(RemediationWorkflowStage.RemediationDecision);
        var pendingDevice = CanonicalTestData.MakeDevice(_tenantId);
        var pendingSoftware = CanonicalTestData.MakeInstalledSoftware(_tenantId, pendingDevice.Id, pendingProduct.Id);

        // Case at RemediationDecision stage WITH a decision → excluded.
        var decidedProduct = SoftwareProduct.Create("Contoso", "Already Decided", null);
        var decidedCase = RemediationCase.Create(_tenantId, decidedProduct.Id);
        var decidedWorkflow = RemediationWorkflow.Create(_tenantId, decidedCase.Id, Guid.NewGuid());
        decidedWorkflow.MoveToStage(RemediationWorkflowStage.RemediationDecision);
        var decidedDecision = RemediationDecision.Create(
            _tenantId,
            decidedCase.Id,
            RemediationOutcome.RiskAcceptance,
            "Accepted",
            _userId
        );
        var decidedDevice = CanonicalTestData.MakeDevice(_tenantId);
        var decidedSoftware = CanonicalTestData.MakeInstalledSoftware(_tenantId, decidedDevice.Id, decidedProduct.Id);

        // Case at SecurityAnalysis stage → excluded (wrong stage).
        var earlyProduct = SoftwareProduct.Create("Contoso", "Still Analyzing", null);
        var earlyCase = RemediationCase.Create(_tenantId, earlyProduct.Id);
        var earlyWorkflow = RemediationWorkflow.Create(_tenantId, earlyCase.Id, Guid.NewGuid());
        var earlyDevice = CanonicalTestData.MakeDevice(_tenantId);
        var earlySoftware = CanonicalTestData.MakeInstalledSoftware(_tenantId, earlyDevice.Id, earlyProduct.Id);

        await _dbContext.AddRangeAsync(
            pendingProduct, pendingCase, pendingWorkflow, pendingDevice, pendingSoftware,
            decidedProduct, decidedCase, decidedWorkflow, decidedDecision, decidedDevice, decidedSoftware,
            earlyProduct, earlyCase, earlyWorkflow, earlyDevice, earlySoftware
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.ListAsync(
            _tenantId,
            new PatchHound.Api.Models.Decisions.RemediationDecisionFilterQuery(NeedsRemediationDecision: true),
            new PaginationQuery(),
            CancellationToken.None
        );

        var item = result.Items.Should().ContainSingle().Subject;
        item.RemediationCaseId.Should().Be(pendingCase.Id);
        item.WorkflowStage.Should().Be(nameof(RemediationWorkflowStage.RemediationDecision));
    }

    [Fact]
    public async Task ListAsync_WhenNeedsApproval_ReturnsApprovalStageCasesWithPendingApproval()
    {
        // Case at Approval stage with PendingApproval decision → matches.
        var pendingApprovalProduct = SoftwareProduct.Create("Contoso", "Awaiting Approval", null);
        var pendingApprovalCase = RemediationCase.Create(_tenantId, pendingApprovalProduct.Id);
        var pendingApprovalWorkflow = RemediationWorkflow.Create(_tenantId, pendingApprovalCase.Id, Guid.NewGuid());
        pendingApprovalWorkflow.MoveToStage(RemediationWorkflowStage.RemediationDecision);
        pendingApprovalWorkflow.MoveToStage(RemediationWorkflowStage.Approval);
        var pendingApprovalDecision = RemediationDecision.Create(
            _tenantId,
            pendingApprovalCase.Id,
            RemediationOutcome.ApprovedForPatching,
            "Patch ready",
            _userId,
            initialApprovalStatus: DecisionApprovalStatus.PendingApproval
        );
        var pendingApprovalDevice = CanonicalTestData.MakeDevice(_tenantId);
        var pendingApprovalSoftware = CanonicalTestData.MakeInstalledSoftware(_tenantId, pendingApprovalDevice.Id, pendingApprovalProduct.Id);

        // Case at Approval stage with already-approved decision → excluded.
        var approvedProduct = SoftwareProduct.Create("Contoso", "Already Approved", null);
        var approvedCase = RemediationCase.Create(_tenantId, approvedProduct.Id);
        var approvedWorkflow = RemediationWorkflow.Create(_tenantId, approvedCase.Id, Guid.NewGuid());
        approvedWorkflow.MoveToStage(RemediationWorkflowStage.RemediationDecision);
        approvedWorkflow.MoveToStage(RemediationWorkflowStage.Approval);
        var approvedDecision = RemediationDecision.Create(
            _tenantId,
            approvedCase.Id,
            RemediationOutcome.ApprovedForPatching,
            "Approved",
            _userId,
            initialApprovalStatus: DecisionApprovalStatus.Approved
        );
        var approvedDevice = CanonicalTestData.MakeDevice(_tenantId);
        var approvedSoftware = CanonicalTestData.MakeInstalledSoftware(_tenantId, approvedDevice.Id, approvedProduct.Id);

        // Case at RemediationDecision stage → excluded (wrong stage).
        var decisionProduct = SoftwareProduct.Create("Contoso", "Still Deciding", null);
        var decisionCase = RemediationCase.Create(_tenantId, decisionProduct.Id);
        var decisionWorkflow = RemediationWorkflow.Create(_tenantId, decisionCase.Id, Guid.NewGuid());
        decisionWorkflow.MoveToStage(RemediationWorkflowStage.RemediationDecision);
        var decisionDevice = CanonicalTestData.MakeDevice(_tenantId);
        var decisionSoftware = CanonicalTestData.MakeInstalledSoftware(_tenantId, decisionDevice.Id, decisionProduct.Id);

        await _dbContext.AddRangeAsync(
            pendingApprovalProduct, pendingApprovalCase, pendingApprovalWorkflow, pendingApprovalDecision,
            pendingApprovalDevice, pendingApprovalSoftware,
            approvedProduct, approvedCase, approvedWorkflow, approvedDecision,
            approvedDevice, approvedSoftware,
            decisionProduct, decisionCase, decisionWorkflow,
            decisionDevice, decisionSoftware
        );
        await _dbContext.SaveChangesAsync();

        var result = await _service.ListAsync(
            _tenantId,
            new PatchHound.Api.Models.Decisions.RemediationDecisionFilterQuery(NeedsApproval: true),
            new PaginationQuery(),
            CancellationToken.None
        );

        var item = result.Items.Should().ContainSingle().Subject;
        item.RemediationCaseId.Should().Be(pendingApprovalCase.Id);
        item.WorkflowStage.Should().Be(nameof(RemediationWorkflowStage.Approval));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
