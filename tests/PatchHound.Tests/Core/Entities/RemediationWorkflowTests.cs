using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using Xunit;

namespace PatchHound.Tests.Core.Entities;

public class RemediationWorkflowTests
{
    [Fact]
    public void Create_takes_tenant_case_and_owner_team()
    {
        var tenantId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        var w = RemediationWorkflow.Create(tenantId, caseId, teamId);

        Assert.Equal(tenantId, w.TenantId);
        Assert.Equal(caseId, w.RemediationCaseId);
        Assert.Equal(teamId, w.SoftwareOwnerTeamId);
        Assert.Equal(RemediationWorkflowStatus.Active, w.Status);
        Assert.Equal(RemediationWorkflowStage.SecurityAnalysis, w.CurrentStage);
    }
}
