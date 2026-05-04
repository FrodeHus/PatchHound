using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.ApprovalTasks;
using PatchHound.Api.Models.Decisions;
using PatchHound.Api.Services;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
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

    private RemediationDecisionsController CreateController(
        RemediationWorkflowAuthorizationService? workflowAuthorizationService = null
    ) =>
        new(
            queryService: null!,
            decisionService: null!,
            approvalTaskService: null!,
            recommendationService: null!,
            workflowAuthorizationService: workflowAuthorizationService!,
            workflowService: null!,
            remediationAiJobService: null!,
            dbContext: _dbContext,
            tenantContext: _tenantContext
        );

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
