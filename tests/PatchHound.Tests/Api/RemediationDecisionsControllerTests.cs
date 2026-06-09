using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.ApprovalTasks;
using PatchHound.Api.Models.Decisions;
using PatchHound.Api.Services;
using PatchHound.Core.Common;
using PatchHound.Core.Constants;
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

    public RemediationDecisionsControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.CurrentUserId.Returns(_userId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);
        _tenantContext.GetRolesForTenant(_tenantId).Returns([RoleName.GlobalAdmin.ToString()]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );
    }

    [Fact]
    public async Task GetAuditTrail_IncludesApprovalTaskResolutionJustification()
    {
        var caseId = Guid.NewGuid();
        var decision = RemediationDecision.Create(
            _tenantId,
            caseId,
            RemediationOutcome.ApprovedForPatching,
            "Owner rationale",
            _userId,
            DecisionApprovalStatus.PendingApproval
        );
        var task = ApprovalTask.Create(
            _tenantId,
            caseId,
            decision.Id,
            RemediationOutcome.ApprovedForPatching,
            ApprovalTaskStatus.Pending,
            DateTimeOffset.UtcNow.AddDays(1)
        );

        _dbContext.RemediationDecisions.Add(decision);
        _dbContext.ApprovalTasks.Add(task);
        _dbContext.AuditLogEntries.Add(AuditLogEntry.Create(
            _tenantId,
            nameof(ApprovalTask),
            task.Id,
            AuditAction.Updated,
            """{"Status":"Pending","ResolutionJustification":null}""",
            """{"Status":"Approved","ResolutionJustification":"Approved for Saturday window"}""",
            _userId
        ));
        await _dbContext.SaveChangesAsync();

        var controller = CreateController();

        var result = await controller.GetAuditTrail(caseId, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var entries = ok.Value.Should().BeAssignableTo<List<ApprovalAuditEntryDto>>().Subject;
        entries.Should().ContainSingle(entry =>
            entry.Action == "Approved"
            && entry.Justification == "Approved for Saturday window");
    }

    [Fact]
    public async Task GetRecommendationContext_ReturnsSnapshotForTenant()
    {
        var caseId = Guid.NewGuid();
        var snapshot = RecommendationContextSnapshot.Create(
            _tenantId, caseId, "{\"contextKind\":\"RemediationCase\"}", "hash", "[]", _userId);
        _dbContext.RecommendationContextSnapshots.Add(snapshot);
        await _dbContext.SaveChangesAsync();

        var controller = CreateController();

        var result = await controller.GetRecommendationContext(caseId, snapshot.Id, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<RecommendationContextSnapshotDto>().Subject;
        dto.Id.Should().Be(snapshot.Id);
        dto.Context.ContextKind.Should().Be("RemediationCase");
    }

    [Fact]
    public async Task GetRecommendationContext_ReturnsNotFoundForUnknownId()
    {
        var controller = CreateController();

        var result = await controller.GetRecommendationContext(
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CreateDecision_RejectsNumericDeadlineMode()
    {
        var caseId = await SeedActiveDecisionWorkflowAsync();
        var controller = CreateController(
            workflowAuthorizationService: new RemediationWorkflowAuthorizationService(_dbContext, _tenantContext)
        );

        var result = await controller.CreateDecision(
            caseId,
            new CreateDecisionRequest(
                Outcome: RemediationOutcome.RiskAcceptance.ToString(),
                Justification: "Risk accepted with invalid deadline mode.",
                MaintenanceWindowDate: null,
                ExpiryDate: null,
                ReEvaluationDate: null,
                DeadlineMode: "999"
            ),
            CancellationToken.None
        );

        var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Invalid deadline mode value.");
    }

    [Fact]
    public async Task GenerateThreatIntel_ReturnsBadRequest_WhenNoTenantSelected()
    {
        var noTenantContext = Substitute.For<ITenantContext>();
        noTenantContext.CurrentTenantId.Returns((Guid?)null);

        var controller = new RemediationDecisionsController(
            queryService: null!,
            decisionService: null!,
            approvalTaskService: null!,
            recommendationService: null!,
            workflowAuthorizationService: null!,
            workflowService: null!,
            threatIntelService: null!,
            aiRecommendationDraftService: null!,
            dbContext: _dbContext,
            tenantContext: noTenantContext
        );

        var result = await controller.GenerateThreatIntel(Guid.NewGuid(), CancellationToken.None);

        var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeOfType<ProblemDetails>().Which.Title.Should().Be("No active tenant is selected.");
    }

    [Fact]
    public async Task GenerateThreatIntel_ReturnsBadRequest_WhenNoAiProfileConfigured()
    {
        var aiResolver = Substitute.For<ITenantAiConfigurationResolver>();
        aiResolver.ResolveDefaultAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(Result<TenantAiProfileResolved>.Failure("No profile."));

        var threatIntelService = new ThreatIntelGenerationService(
            _dbContext,
            new TenantAiTextGenerationService([], aiResolver),
            aiResolver
        );

        var controller = new RemediationDecisionsController(
            queryService: null!,
            decisionService: null!,
            approvalTaskService: null!,
            recommendationService: null!,
            workflowAuthorizationService: null!,
            workflowService: null!,
            threatIntelService: threatIntelService,
            aiRecommendationDraftService: null!,
            dbContext: _dbContext,
            tenantContext: _tenantContext
        );

        var result = await controller.GenerateThreatIntel(Guid.NewGuid(), CancellationToken.None);

        var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeOfType<ProblemDetails>().Which.Title.Should()
            .Be("No enabled default AI profile is configured for this tenant.");
    }

    [Fact]
    public async Task GenerateThreatIntel_ReturnsNotFound_WhenCaseDoesNotExist()
    {
        var aiResolver = Substitute.For<ITenantAiConfigurationResolver>();
        aiResolver.ResolveDefaultAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(Result<TenantAiProfileResolved>.Success(null!));

        var threatIntelService = new ThreatIntelGenerationService(
            _dbContext,
            new TenantAiTextGenerationService([], aiResolver),
            aiResolver
        );

        var controller = new RemediationDecisionsController(
            queryService: null!,
            decisionService: null!,
            approvalTaskService: null!,
            recommendationService: null!,
            workflowAuthorizationService: null!,
            workflowService: null!,
            threatIntelService: threatIntelService,
            aiRecommendationDraftService: null!,
            dbContext: _dbContext,
            tenantContext: _tenantContext
        );

        var result = await controller.GenerateThreatIntel(Guid.NewGuid(), CancellationToken.None);

        var notFound = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.Value.Should().BeOfType<ProblemDetails>().Which.Title.Should()
            .Be("Remediation case not found.");
    }

    [Fact]
    public async Task GenerateThreatIntel_PersistsSummaryForDecisionContextReload()
    {
        var product = SoftwareProduct.Create("Contoso", "Contoso Agent", null);
        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        var device = CanonicalTestData.MakeDevice(_tenantId);
        var installedSoftware = CanonicalTestData.MakeInstalledSoftware(_tenantId, device.Id, product.Id);
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
            DateTimeOffset.UtcNow.AddDays(-2),
            runId: Guid.NewGuid());
        var profile = TenantAiProfileFactory.Create(_tenantId, name: "Threat profile");
        var provider = Substitute.For<IAiReportProvider>();
        provider.ProviderType.Returns(TenantAiProviderType.OpenAi);
        provider
            .GenerateTextAsync(
                Arg.Any<AiTextGenerationRequest>(),
                Arg.Any<TenantAiProfileResolved>(),
                Arg.Any<CancellationToken>())
            .Returns("Persisted threat intelligence.");
        var aiResolver = Substitute.For<ITenantAiConfigurationResolver>();
        aiResolver.ResolveDefaultAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(Result<TenantAiProfileResolved>.Success(new TenantAiProfileResolved(profile, "secret")));

        await _dbContext.AddRangeAsync(product, remediationCase, device, installedSoftware, vulnerability, exposure);
        await _dbContext.SaveChangesAsync();

        var threatIntelService = new ThreatIntelGenerationService(
            _dbContext,
            new TenantAiTextGenerationService([provider], aiResolver),
            aiResolver
        );
        var generated = await threatIntelService.GenerateAsync(
            _tenantId,
            remediationCase.Id,
            CancellationToken.None
        );

        generated.IsSuccess.Should().BeTrue(generated.Error);
        _dbContext.ChangeTracker.Clear();

        var queryService = new RemediationDecisionQueryService(
            _dbContext,
            new SlaService(),
            aiResolver,
            _tenantContext
        );
        var reloaded = await queryService.BuildByCaseIdAsync(
            _tenantId,
            remediationCase.Id,
            CancellationToken.None
        );

        reloaded.Should().NotBeNull();
        reloaded!.ThreatIntel.Summary.Should().Be("Persisted threat intelligence.");
        reloaded.ThreatIntel.GeneratedAt.Should().NotBeNull();
        reloaded.ThreatIntel.ProfileName.Should().Be("Threat profile");
    }

    [Fact]
    public async Task GenerateAiRecommendationDraft_ReturnsBadRequest_WhenNoTenantSelected()
    {
        var noTenantContext = Substitute.For<ITenantContext>();
        noTenantContext.CurrentTenantId.Returns((Guid?)null);

        var controller = new RemediationDecisionsController(
            queryService: null!,
            decisionService: null!,
            approvalTaskService: null!,
            recommendationService: null!,
            workflowAuthorizationService: null!,
            workflowService: null!,
            threatIntelService: null!,
            aiRecommendationDraftService: null!,
            dbContext: _dbContext,
            tenantContext: noTenantContext
        );

        var result = await controller.GenerateAiRecommendationDraft(Guid.NewGuid(), CancellationToken.None);

        var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeOfType<ProblemDetails>().Which.Title.Should().Be("No active tenant is selected.");
    }

    [Fact]
    public async Task GenerateAiRecommendationDraft_ReturnsNotFound_WhenCaseDoesNotExist()
    {
        var service = CreateAiRecommendationDraftService();
        var controller = CreateController(aiRecommendationDraftService: service);

        var result = await controller.GenerateAiRecommendationDraft(Guid.NewGuid(), CancellationToken.None);

        var notFound = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.Value.Should().BeOfType<ProblemDetails>().Which.Title.Should()
            .Be("Remediation case not found.");
    }

    [Fact]
    public async Task GenerateAiRecommendationDraft_ReturnsDraftFromPatchAssessments()
    {
        var product = SoftwareProduct.Create("Contoso", "Contoso Agent", null);
        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        var device = CanonicalTestData.MakeDevice(_tenantId);
        var installedSoftware = CanonicalTestData.MakeInstalledSoftware(_tenantId, device.Id, product.Id);
        var vulnerability = Vulnerability.Create(
            "nvd",
            "CVE-2026-4242",
            "Remote code execution",
            "A remotely exploitable vulnerability.",
            Severity.Critical,
            9.8m,
            null,
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
            DateTimeOffset.UtcNow.AddDays(-2),
            runId: Guid.NewGuid());
        var assessment = VulnerabilityPatchAssessment.Create(
            vulnerability.Id,
            "Patch immediately.",
            "High",
            "Known exploitation is credible.",
            PatchUrgencyTier.Emergency,
            "Within 24 hours",
            "Public exploitation and high blast radius.",
            "[]",
            "[]",
            "[]",
            "Default AI",
            null,
            DateTimeOffset.UtcNow
        );
        var profile = TenantAiProfileFactory.Create(_tenantId, name: "Recommendation profile");
        var provider = Substitute.For<IAiReportProvider>();
        provider.ProviderType.Returns(TenantAiProviderType.OpenAi);
        provider
            .GenerateTextAsync(
                Arg.Is<AiTextGenerationRequest>(request =>
                    request.UserPrompt.Contains("CVE-2026-4242")
                    && request.UserPrompt.Contains("Public exploitation and high blast radius.")
                    && request.UserPrompt.Contains("Target SLA: Within 24 hours")),
                Arg.Any<TenantAiProfileResolved>(),
                Arg.Any<CancellationToken>())
            .Returns("""
            {
              "recommendedOutcome": "ApprovedForPatching",
              "priorityOverride": "Critical",
              "rationale": "Patch immediately because exploitation is likely and the target SLA is within 24 hours."
            }
            """);
        var aiResolver = Substitute.For<ITenantAiConfigurationResolver>();
        aiResolver.ResolveDefaultAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(Result<TenantAiProfileResolved>.Success(new TenantAiProfileResolved(profile, "secret")));

        await _dbContext.AddRangeAsync(product, remediationCase, device, installedSoftware, vulnerability, exposure, assessment);
        await _dbContext.SaveChangesAsync();

        var service = CreateAiRecommendationDraftService(provider, aiResolver);
        var controller = CreateController(aiRecommendationDraftService: service);

        var result = await controller.GenerateAiRecommendationDraft(remediationCase.Id, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var draft = ok.Value.Should().BeOfType<AiRecommendationDraftDto>().Subject;
        draft.RecommendedOutcome.Should().Be("ApprovedForPatching");
        draft.PriorityOverride.Should().Be("Critical");
        draft.Rationale.Should().Contain("target SLA is within 24 hours");
    }

    private RemediationDecisionsController CreateController(
        RemediationWorkflowAuthorizationService? workflowAuthorizationService = null,
        AiRecommendationDraftService? aiRecommendationDraftService = null
    ) =>
        new(
            queryService: null!,
            decisionService: null!,
            approvalTaskService: null!,
            recommendationService: null!,
            workflowAuthorizationService: workflowAuthorizationService!,
            workflowService: null!,
            threatIntelService: null!,
            aiRecommendationDraftService: aiRecommendationDraftService!,
            dbContext: _dbContext,
            tenantContext: _tenantContext
        );

    private AiRecommendationDraftService CreateAiRecommendationDraftService(
        IAiReportProvider? provider = null,
        ITenantAiConfigurationResolver? aiResolver = null)
    {
        aiResolver ??= Substitute.For<ITenantAiConfigurationResolver>();
        if (provider is null)
        {
            var profile = TenantAiProfileFactory.Create(_tenantId, name: "Recommendation profile");
            aiResolver.ResolveDefaultAsync(_tenantId, Arg.Any<CancellationToken>())
                .Returns(Result<TenantAiProfileResolved>.Success(new TenantAiProfileResolved(profile, "secret")));
            provider = Substitute.For<IAiReportProvider>();
            provider.ProviderType.Returns(TenantAiProviderType.OpenAi);
        }

        return new AiRecommendationDraftService(
            _dbContext,
            new TenantAiTextGenerationService([provider], aiResolver),
            aiResolver,
            Substitute.For<IAiOperationalContextService>()
        );
    }

    private async Task<Guid> SeedActiveDecisionWorkflowAsync()
    {
        var product = SoftwareProduct.Create("Contoso", "Contoso Agent", null);
        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        var workflow = RemediationWorkflow.Create(
            _tenantId,
            remediationCase.Id,
            Guid.NewGuid(),
            RemediationWorkflowStage.RemediationDecision
        );

        await _dbContext.AddRangeAsync(product, remediationCase, workflow);
        await _dbContext.SaveChangesAsync();
        return remediationCase.Id;
    }

    public void Dispose() => _dbContext.Dispose();
}
