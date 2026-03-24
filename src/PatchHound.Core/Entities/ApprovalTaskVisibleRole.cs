using PatchHound.Core.Enums;

namespace PatchHound.Core.Entities;

public class ApprovalTaskVisibleRole
{
    public Guid Id { get; private set; }
    public Guid ApprovalTaskId { get; private set; }
    public RoleName Role { get; private set; }

    public ApprovalTask ApprovalTask { get; private set; } = null!;

    private ApprovalTaskVisibleRole() { }

    internal static ApprovalTaskVisibleRole Create(RoleName role)
    {
        return new ApprovalTaskVisibleRole
        {
            Id = Guid.NewGuid(),
            Role = role,
        };
    }

    internal void AttachToTask(Guid approvalTaskId)
    {
        ApprovalTaskId = approvalTaskId;
    }
}
