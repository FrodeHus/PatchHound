using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure;

public class EmailNotificationServiceTests : IDisposable
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly IEmailSender _emailSender;
    private readonly EmailNotificationService _sut;
    private readonly ITenantContext _tenantContext;
    private readonly Guid _tenantId = Guid.NewGuid();

    public EmailNotificationServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        _tenantContext.CurrentTenantId.Returns(_tenantId);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );
        _emailSender = Substitute.For<IEmailSender>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FRONTEND_ORIGIN"] = "https://app.patchhound.test" })
            .Build();
        _sut = new EmailNotificationService(
            _dbContext,
            _emailSender,
            configuration,
            NullLogger<EmailNotificationService>.Instance
        );
    }

    [Fact]
    public async Task SendAsync_CreatesNotificationRecord_AndSendsEmail()
    {
        // Arrange
        var user = User.Create("alice@example.com", "Alice", "entra-1");
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.SendAsync(
            user.Id,
            _tenantId,
            NotificationType.TaskAssigned,
            "Task Assigned",
            "<p>You have a new task.</p>",
            "PatchingTask",
            Guid.NewGuid()
        );

        // Assert
        var notifications = await _dbContext.Notifications.IgnoreQueryFilters().ToListAsync();
        notifications.Should().ContainSingle();

        var notification = notifications[0];
        notification.UserId.Should().Be(user.Id);
        notification.TenantId.Should().Be(_tenantId);
        notification.Type.Should().Be(NotificationType.TaskAssigned);
        notification.Title.Should().Be("Task Assigned");
        notification.Body.Should().Be("<p>You have a new task.</p>");
        notification.RelatedEntityType.Should().Be("PatchingTask");

        await _emailSender
            .Received(1)
            .SendEmailAsync(
                "alice@example.com",
                "Task Assigned",
                Arg.Is<string>(html =>
                    html.Contains("Task Assigned")
                    && html.Contains("&lt;p&gt;You have a new task.&lt;/p&gt;")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SendAsync_WhenUserNotFound_CreatesNotification_ButDoesNotSendEmail()
    {
        // Arrange
        var nonExistentUserId = Guid.NewGuid();

        // Act
        await _sut.SendAsync(
            nonExistentUserId,
            _tenantId,
            NotificationType.SLAWarning,
            "SLA Warning",
            "Approaching SLA deadline"
        );

        // Assert
        var notifications = await _dbContext.Notifications.IgnoreQueryFilters().ToListAsync();
        notifications.Should().ContainSingle();

        await _emailSender
            .DidNotReceive()
            .SendEmailAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SendToTeamAsync_SendsNotificationAndEmail_ToAllTeamMembers()
    {
        // Arrange
        var user1 = User.Create("alice@example.com", "Alice", "entra-1");
        var user2 = User.Create("bob@example.com", "Bob", "entra-2");
        _dbContext.Users.AddRange(user1, user2);

        var team = Team.Create(_tenantId, "Security Team");
        _dbContext.Teams.Add(team);

        var member1 = TeamMember.Create(team.Id, user1.Id);
        var member2 = TeamMember.Create(team.Id, user2.Id);
        _dbContext.TeamMembers.AddRange(member1, member2);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.SendToTeamAsync(
            team.Id,
            _tenantId,
            NotificationType.NewCriticalVuln,
            "Critical Vulnerability",
            "<p>A new critical vulnerability has been discovered.</p>"
        );

        // Assert
        var notifications = await _dbContext.Notifications.IgnoreQueryFilters().ToListAsync();
        notifications.Should().HaveCount(2);
        notifications.Select(n => n.UserId).Should().BeEquivalentTo([user1.Id, user2.Id]);

        await _emailSender
            .Received(1)
            .SendEmailAsync(
                "alice@example.com",
                "Critical Vulnerability",
                Arg.Is<string>(html =>
                    html.Contains("Critical Vulnerability")
                    && html.Contains("&lt;p&gt;A new critical vulnerability has been discovered.&lt;/p&gt;")),
                Arg.Any<CancellationToken>()
            );

        await _emailSender
            .Received(1)
            .SendEmailAsync(
                "bob@example.com",
                "Critical Vulnerability",
                Arg.Is<string>(html =>
                    html.Contains("Critical Vulnerability")
                    && html.Contains("&lt;p&gt;A new critical vulnerability has been discovered.&lt;/p&gt;")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SendToTeamAsync_WithNoMembers_SendsNoNotifications()
    {
        // Arrange
        var team = Team.Create(_tenantId, "Empty Team");
        _dbContext.Teams.Add(team);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.SendToTeamAsync(
            team.Id,
            _tenantId,
            NotificationType.RiskAcceptanceRequired,
            "Risk Acceptance Required",
            "Please review"
        );

        // Assert
        var notifications = await _dbContext.Notifications.IgnoreQueryFilters().ToListAsync();
        notifications.Should().BeEmpty();

        await _emailSender
            .DidNotReceive()
            .SendEmailAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SendAsync_WhenEmailDeliveryFails_PersistsNotification_AndDoesNotThrow()
    {
        var user = User.Create("alice@example.com", "Alice", "entra-1");
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        _emailSender
            .SendEmailAsync(
                "alice@example.com",
                "Task Assigned",
                "<p>You have a new task.</p>",
                Arg.Any<CancellationToken>()
            )
            .Returns<Task>(_ => throw new InvalidOperationException("SMTP unavailable"));

        var act = () => _sut.SendAsync(
            user.Id,
            _tenantId,
            NotificationType.TaskAssigned,
            "Task Assigned",
            "<p>You have a new task.</p>",
            "PatchingTask",
            Guid.NewGuid()
        );

        await act.Should().NotThrowAsync();

        var notifications = await _dbContext.Notifications.IgnoreQueryFilters().ToListAsync();
        notifications.Should().ContainSingle();
    }

    [Fact]
    public async Task SendAsync_ForApprovalTask_RendersRemediationHtmlSummary()
    {
        var user = User.Create("alice@example.com", "Alice", "entra-1");
        _dbContext.Users.Add(user);

        // Phase 4 (#17): seed SoftwareProduct + RemediationCase instead of TenantSoftware graph
        var product = SoftwareProduct.Create("contoso", "agent", "contoso:agent");
        var remCase = RemediationCase.Create(_tenantId, product.Id);
        _dbContext.SoftwareProducts.Add(product);
        _dbContext.RemediationCases.Add(remCase);

        var decision = RemediationDecision.Create(
            _tenantId,
            remCase.Id,
            RemediationOutcome.RiskAcceptance,
            "Temporary exception proposed.",
            Guid.NewGuid(),
            DecisionApprovalStatus.PendingApproval,
            DateTimeOffset.UtcNow.AddDays(14)
        );
        var approvalTask = ApprovalTask.Create(
            _tenantId,
            remCase.Id,
            decision.Id,
            RemediationOutcome.RiskAcceptance,
            ApprovalTaskStatus.Pending,
            DateTimeOffset.UtcNow.AddHours(12)
        );
        _dbContext.RemediationDecisions.Add(decision);
        _dbContext.ApprovalTasks.Add(approvalTask);
        await _dbContext.SaveChangesAsync();

        await _sut.SendAsync(
            user.Id,
            _tenantId,
            NotificationType.ApprovalTaskCreated,
            "Approval required: Remediation decision",
            "A remediation decision requires your attention.",
            "ApprovalTask",
            approvalTask.Id
        );

        await _emailSender.Received(1).SendEmailAsync(
            "alice@example.com",
            "Approval required: Remediation decision",
            Arg.Is<string>(body =>
                body.Contains("agent")
                && body.Contains("Severity:")
                && body.Contains("Affected devices: 0") // Phase 5 TODO: will be actual count
                && body.Contains("Open approval task")
                && body.Contains("https://app.patchhound.test/approvals/")),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task SendAsync_ForPatchingTask_RendersExecutionHtmlSummary()
    {
        var user = User.Create("alice@example.com", "Alice", "entra-1");
        _dbContext.Users.Add(user);
        var team = Team.Create(_tenantId, "Infrastructure");
        _dbContext.Teams.Add(team);
        _dbContext.TeamMembers.Add(TeamMember.Create(team.Id, user.Id));

        // Phase 4 (#17): seed SoftwareProduct + RemediationCase instead of TenantSoftware graph
        var product = SoftwareProduct.Create("contoso", "agent", "contoso:agent");
        var remCase = RemediationCase.Create(_tenantId, product.Id);
        _dbContext.SoftwareProducts.Add(product);
        _dbContext.RemediationCases.Add(remCase);

        var decision = RemediationDecision.Create(
            _tenantId,
            remCase.Id,
            RemediationOutcome.ApprovedForPatching,
            null,
            Guid.NewGuid(),
            DecisionApprovalStatus.Approved
        );
        var patchingTask = PatchingTask.Create(
            _tenantId,
            remCase.Id,
            decision.Id,
            team.Id,
            DateTimeOffset.UtcNow.AddDays(7)
        );

        _dbContext.RemediationDecisions.Add(decision);
        _dbContext.PatchingTasks.Add(patchingTask);
        await _dbContext.SaveChangesAsync();

        await _sut.SendAsync(
            user.Id,
            _tenantId,
            NotificationType.TaskAssigned,
            "Remediation task assigned: agent",
            "Patch agent. Highest severity: Critical. Affected devices for your team: 2.",
            "PatchingTask",
            patchingTask.Id
        );

        await _emailSender.Received(1).SendEmailAsync(
            "alice@example.com",
            "Remediation task assigned: agent",
            Arg.Is<string>(body =>
                body.Contains("Stage: Execution")
                && body.Contains("Open remediation tasks")
                && body.Contains("https://app.patchhound.test/remediation/tasks?caseId=")), // Phase 4 (#17): was tenantSoftwareId
            Arg.Any<CancellationToken>()
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
