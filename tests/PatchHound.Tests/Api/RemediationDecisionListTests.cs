using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Models;
using PatchHound.Api.Services;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
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
        _service = new RemediationDecisionQueryService(
            _dbContext,
            new SlaService(),
            new TenantAiTextGenerationService([], aiConfigurationResolver),
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
            forceAiSummaryRefresh: false,
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

        await _dbContext.AddRangeAsync(ownerTeam, product, tenantSoftware, remediationCase, workflow);
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

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
