using FluentAssertions;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;

namespace PatchHound.Tests.Core;

public class RemediationTaskServiceTests
{
    private readonly IRemediationTaskRepository _taskRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly RemediationTaskService _service;
    private readonly Guid _tenantId = Guid.NewGuid();

    public RemediationTaskServiceTests()
    {
        _taskRepo = Substitute.For<IRemediationTaskRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _service = new RemediationTaskService(_taskRepo, _unitOfWork);
    }

    private RemediationTask CreateTask(
        RemediationTaskStatus initialStatus = RemediationTaskStatus.Pending
    )
    {
        var task = RemediationTask.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            _tenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(7)
        );
        if (initialStatus != RemediationTaskStatus.Pending)
            task.UpdateStatus(initialStatus);
        return task;
    }

    [Fact]
    public async Task Pending_To_InProgress_Succeeds()
    {
        var task = CreateTask();
        _taskRepo.GetByIdAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        var result = await _service.UpdateStatusAsync(
            task.Id,
            RemediationTaskStatus.InProgress,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(RemediationTaskStatus.InProgress);
    }

    [Fact]
    public async Task Pending_To_Completed_Fails()
    {
        var task = CreateTask();
        _taskRepo.GetByIdAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        var result = await _service.UpdateStatusAsync(
            task.Id,
            RemediationTaskStatus.Completed,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Invalid transition");
    }

    [Fact]
    public async Task CannotPatch_WithoutJustification_Fails()
    {
        var task = CreateTask(RemediationTaskStatus.InProgress);
        _taskRepo.GetByIdAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        var result = await _service.UpdateStatusAsync(
            task.Id,
            RemediationTaskStatus.CannotPatch,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Justification is required");
    }

    [Fact]
    public async Task CannotPatch_WithJustification_Succeeds()
    {
        var task = CreateTask(RemediationTaskStatus.InProgress);
        _taskRepo.GetByIdAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        var result = await _service.UpdateStatusAsync(
            task.Id,
            RemediationTaskStatus.CannotPatch,
            "Legacy system, no patch available",
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(RemediationTaskStatus.CannotPatch);
    }

    [Fact]
    public async Task RiskAccepted_WithoutJustification_Fails()
    {
        var task = CreateTask(RemediationTaskStatus.InProgress);
        _taskRepo.GetByIdAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        var result = await _service.UpdateStatusAsync(
            task.Id,
            RemediationTaskStatus.RiskAccepted,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Justification is required");
    }

    [Fact]
    public async Task Completed_CannotTransition_Further()
    {
        var task = CreateTask(RemediationTaskStatus.InProgress);
        task.UpdateStatus(RemediationTaskStatus.PatchScheduled);
        task.UpdateStatus(RemediationTaskStatus.Completed);
        _taskRepo.GetByIdAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        var result = await _service.UpdateStatusAsync(
            task.Id,
            RemediationTaskStatus.InProgress,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Invalid transition");
    }

    [Fact]
    public async Task TaskNotFound_ReturnsFailure()
    {
        _taskRepo
            .GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RemediationTask?)null);

        var result = await _service.UpdateStatusAsync(
            Guid.NewGuid(),
            RemediationTaskStatus.InProgress,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task RiskAccepted_FromPending_WithJustification_Succeeds()
    {
        var task = CreateTask();
        _taskRepo.GetByIdAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        var result = await _service.UpdateStatusAsync(
            task.Id,
            RemediationTaskStatus.RiskAccepted,
            "Accepted by management",
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(RemediationTaskStatus.RiskAccepted);
    }

    [Fact]
    public async Task PatchScheduled_To_Completed_Succeeds()
    {
        var task = CreateTask(RemediationTaskStatus.InProgress);
        task.UpdateStatus(RemediationTaskStatus.PatchScheduled);
        _taskRepo.GetByIdAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        var result = await _service.UpdateStatusAsync(
            task.Id,
            RemediationTaskStatus.Completed,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(RemediationTaskStatus.Completed);
    }

    [Fact]
    public async Task RiskAccepted_CannotTransition_Further()
    {
        var task = CreateTask();
        task.UpdateStatus(RemediationTaskStatus.RiskAccepted, "Accepted");
        _taskRepo.GetByIdAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        var result = await _service.UpdateStatusAsync(
            task.Id,
            RemediationTaskStatus.InProgress,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Invalid transition");
    }

    [Fact]
    public async Task CannotPatch_To_InProgress_Succeeds()
    {
        var task = CreateTask(RemediationTaskStatus.InProgress);
        task.UpdateStatus(RemediationTaskStatus.CannotPatch, "No patch yet");
        _taskRepo.GetByIdAsync(task.Id, Arg.Any<CancellationToken>()).Returns(task);

        var result = await _service.UpdateStatusAsync(
            task.Id,
            RemediationTaskStatus.InProgress,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(RemediationTaskStatus.InProgress);
    }
}
