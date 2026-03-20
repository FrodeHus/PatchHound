namespace PatchHound.Core.Enums;

public enum WorkflowNodeType
{
    Start,
    AssignGroup,
    WaitForAction,
    SendNotification,
    Condition,
    SystemTask,
    Merge,
    End,
}
