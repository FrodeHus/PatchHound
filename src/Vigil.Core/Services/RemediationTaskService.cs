using Vigil.Core.Common;
using Vigil.Core.Entities;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;

namespace Vigil.Core.Services;

public class RemediationTaskService
{
    private readonly IRemediationTaskRepository _taskRepository;
    private readonly IUnitOfWork _unitOfWork;

    // Valid state transitions
    private static readonly Dictionary<
        RemediationTaskStatus,
        HashSet<RemediationTaskStatus>
    > ValidTransitions = new()
    {
        [RemediationTaskStatus.Pending] = new()
        {
            RemediationTaskStatus.InProgress,
            RemediationTaskStatus.RiskAccepted,
        },
        [RemediationTaskStatus.InProgress] = new()
        {
            RemediationTaskStatus.PatchScheduled,
            RemediationTaskStatus.CannotPatch,
            RemediationTaskStatus.RiskAccepted,
        },
        [RemediationTaskStatus.PatchScheduled] = new()
        {
            RemediationTaskStatus.Completed,
            RemediationTaskStatus.RiskAccepted,
        },
        [RemediationTaskStatus.CannotPatch] = new()
        {
            RemediationTaskStatus.RiskAccepted,
            RemediationTaskStatus.InProgress,
        },
        [RemediationTaskStatus.Completed] = new(),
        [RemediationTaskStatus.RiskAccepted] = new(),
    };

    private static readonly HashSet<RemediationTaskStatus> RequiresJustification = new()
    {
        RemediationTaskStatus.CannotPatch,
        RemediationTaskStatus.RiskAccepted,
    };

    public RemediationTaskService(IRemediationTaskRepository taskRepository, IUnitOfWork unitOfWork)
    {
        _taskRepository = taskRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<RemediationTask>> UpdateStatusAsync(
        Guid taskId,
        RemediationTaskStatus newStatus,
        string? justification,
        CancellationToken ct
    )
    {
        var task = await _taskRepository.GetByIdAsync(taskId, ct);
        if (task is null)
            return Result<RemediationTask>.Failure("Task not found");

        if (
            !ValidTransitions.TryGetValue(task.Status, out var allowed)
            || !allowed.Contains(newStatus)
        )
            return Result<RemediationTask>.Failure(
                $"Invalid transition from {task.Status} to {newStatus}"
            );

        if (RequiresJustification.Contains(newStatus) && string.IsNullOrWhiteSpace(justification))
            return Result<RemediationTask>.Failure(
                $"Justification is required for status {newStatus}"
            );

        task.UpdateStatus(newStatus, justification);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<RemediationTask>.Success(task);
    }
}
