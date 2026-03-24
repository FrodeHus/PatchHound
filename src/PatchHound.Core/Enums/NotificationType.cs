namespace PatchHound.Core.Enums;

public enum NotificationType
{
    TaskAssigned,
    SLAWarning,
    NewCriticalVuln,
    RiskAcceptanceRequired,
    RiskAcceptanceDecision,
    TaskStatusChanged,
    WorkflowNotification,
    ApprovalTaskCreated,
    ApprovalTaskDenied,
    ApprovalTaskAutoExpired,
}
