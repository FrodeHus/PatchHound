namespace PatchHound.Core.Enums;

public enum RemediationTaskStatus
{
    Pending,
    InProgress,
    PatchScheduled,
    CannotPatch,
    Completed,
    RiskAccepted,
}
