namespace PatchHound.Core.Enums;

public static class DecisionApprovalStatusExtensions
{
    public static bool IsTerminal(this DecisionApprovalStatus status) =>
        status is DecisionApprovalStatus.Rejected or DecisionApprovalStatus.Expired;
}
