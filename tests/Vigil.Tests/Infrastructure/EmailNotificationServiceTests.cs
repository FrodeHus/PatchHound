using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Vigil.Core.Entities;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;
using Vigil.Infrastructure.Data;
using Vigil.Infrastructure.Services;

namespace Vigil.Tests.Infrastructure;

public class EmailNotificationServiceTests : IDisposable
{
    private readonly VigilDbContext _dbContext;
    private readonly IEmailSender _emailSender;
    private readonly EmailNotificationService _sut;
    private readonly ITenantContext _tenantContext;
    private readonly Guid _tenantId = Guid.NewGuid();

    public EmailNotificationServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        _tenantContext.CurrentTenantId.Returns(_tenantId);

        var options = new DbContextOptionsBuilder<VigilDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new VigilDbContext(options, _tenantContext);
        _emailSender = Substitute.For<IEmailSender>();
        _sut = new EmailNotificationService(_dbContext, _emailSender);
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
            "RemediationTask",
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
        notification.RelatedEntityType.Should().Be("RemediationTask");

        await _emailSender.Received(1).SendEmailAsync(
            "alice@example.com",
            "Task Assigned",
            "<p>You have a new task.</p>",
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

        await _emailSender.DidNotReceive().SendEmailAsync(
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

        await _emailSender.Received(1).SendEmailAsync(
            "alice@example.com",
            "Critical Vulnerability",
            "<p>A new critical vulnerability has been discovered.</p>",
            Arg.Any<CancellationToken>()
        );

        await _emailSender.Received(1).SendEmailAsync(
            "bob@example.com",
            "Critical Vulnerability",
            "<p>A new critical vulnerability has been discovered.</p>",
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

        await _emailSender.DidNotReceive().SendEmailAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>()
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
