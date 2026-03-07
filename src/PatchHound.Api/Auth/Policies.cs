namespace PatchHound.Api.Auth;

public static class Policies
{
    public const string ViewVulnerabilities = nameof(ViewVulnerabilities);
    public const string ModifyVulnerabilities = nameof(ModifyVulnerabilities);
    public const string AdjustSeverity = nameof(AdjustSeverity);
    public const string AssignTasks = nameof(AssignTasks);
    public const string UpdateTaskStatus = nameof(UpdateTaskStatus);
    public const string RequestRiskAcceptance = nameof(RequestRiskAcceptance);
    public const string ApproveRiskAcceptance = nameof(ApproveRiskAcceptance);
    public const string ViewAuditLogs = nameof(ViewAuditLogs);
    public const string ManageUsers = nameof(ManageUsers);
    public const string ConfigureTenant = nameof(ConfigureTenant);
    public const string GenerateAiReports = nameof(GenerateAiReports);
    public const string AddComments = nameof(AddComments);
    public const string ManageTeams = nameof(ManageTeams);
    public const string ManageVault = nameof(ManageVault);
}
