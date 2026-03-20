namespace PatchHound.Core.Enums;

public enum WorkflowNodeExecutionStatus
{
    Pending,
    Running,
    WaitingForAction,
    Completed,
    Failed,
    Skipped,
}
