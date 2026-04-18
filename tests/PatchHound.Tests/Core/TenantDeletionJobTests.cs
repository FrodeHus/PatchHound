using FluentAssertions;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Tests.Core;

public class TenantDeletionJobTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Create_sets_Pending_status_and_timestamps()
    {
        var before = DateTimeOffset.UtcNow;
        var job = TenantDeletionJob.Create(TenantId, UserId);
        var after = DateTimeOffset.UtcNow;

        job.TenantId.Should().Be(TenantId);
        job.RequestedByUserId.Should().Be(UserId);
        job.Status.Should().Be(TenantDeletionJobStatus.Pending);
        job.StartedAt.Should().BeNull();
        job.CompletedAt.Should().BeNull();
        job.Error.Should().BeNull();
        job.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void MarkRunning_sets_Running_and_StartedAt()
    {
        var job = TenantDeletionJob.Create(TenantId, UserId);
        var before = DateTimeOffset.UtcNow;
        job.MarkRunning();
        var after = DateTimeOffset.UtcNow;

        job.Status.Should().Be(TenantDeletionJobStatus.Running);
        job.StartedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void MarkCompleted_sets_Completed_and_CompletedAt()
    {
        var job = TenantDeletionJob.Create(TenantId, UserId);
        job.MarkRunning();
        var before = DateTimeOffset.UtcNow;
        job.MarkCompleted();
        var after = DateTimeOffset.UtcNow;

        job.Status.Should().Be(TenantDeletionJobStatus.Completed);
        job.CompletedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        job.Error.Should().BeNull();
    }

    [Fact]
    public void MarkFailed_sets_Failed_and_Error()
    {
        var job = TenantDeletionJob.Create(TenantId, UserId);
        job.MarkRunning();
        job.MarkFailed("Something went wrong");

        job.Status.Should().Be(TenantDeletionJobStatus.Failed);
        job.Error.Should().Be("Something went wrong");
        job.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Create_throws_when_tenantId_is_empty()
    {
        var act = () => TenantDeletionJob.Create(Guid.Empty, UserId);
        act.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Fact]
    public void Create_throws_when_requestedByUserId_is_empty()
    {
        var act = () => TenantDeletionJob.Create(TenantId, Guid.Empty);
        act.Should().Throw<ArgumentException>().WithParameterName("requestedByUserId");
    }

    [Fact]
    public void Reset_throws_when_requestedByUserId_is_empty()
    {
        var job = TenantDeletionJob.Create(TenantId, UserId);
        var act = () => job.Reset(Guid.Empty);
        act.Should().Throw<ArgumentException>().WithParameterName("requestedByUserId");
    }

    [Fact]
    public void Reset_throws_when_job_is_Running()
    {
        var job = TenantDeletionJob.Create(TenantId, UserId);
        job.MarkRunning();
        var act = () => job.Reset(Guid.NewGuid());
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Reset_restores_Pending_and_clears_run_fields()
    {
        var job = TenantDeletionJob.Create(TenantId, UserId);
        job.MarkRunning();
        job.MarkFailed("error");
        var newUser = Guid.NewGuid();

        job.Reset(newUser);

        job.Status.Should().Be(TenantDeletionJobStatus.Pending);
        job.RequestedByUserId.Should().Be(newUser);
        job.StartedAt.Should().BeNull();
        job.CompletedAt.Should().BeNull();
        job.Error.Should().BeNull();
    }
}
