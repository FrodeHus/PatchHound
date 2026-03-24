using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure;

public class RemediationDecisionServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly RemediationDecisionService _sut;

    public RemediationDecisionServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.CurrentUserId.Returns(_userId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );

        var approvalTaskService = new ApprovalTaskService(
            _dbContext,
            Substitute.For<INotificationService>(),
            Substitute.For<IRealTimeNotifier>()
        );
        _sut = new RemediationDecisionService(
            _dbContext,
            new SlaService(),
            approvalTaskService
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task ReconcileResolvedSoftwareRemediationsAsync_ClosesDecisionAndTasks_WhenNoUnresolvedVulnerabilitiesRemain()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var team = Team.Create(_tenantId, "Platform");
        await _dbContext.Teams.AddAsync(team);

        var devices = await _dbContext.Assets
            .Where(item => item.AssetType == AssetType.Device)
            .ToListAsync();
        foreach (var device in devices)
        {
            device.AssignTeamOwner(team.Id);
        }

        await _dbContext.SaveChangesAsync();

        var softwareAssetId = await _dbContext.NormalizedSoftwareInstallations
            .Where(item => item.TenantSoftwareId == graph.TenantSoftware.Id)
            .Select(item => item.SoftwareAssetId)
            .OrderBy(id => id)
            .FirstAsync();

        var createResult = await _sut.CreateDecisionAsync(
            _tenantId,
            softwareAssetId,
            RemediationOutcome.ApprovedForPatching,
            "Patch it",
            _userId,
            null,
            null,
            CancellationToken.None
        );

        createResult.IsSuccess.Should().BeTrue();

        var openMatches = await _dbContext.SoftwareVulnerabilityMatches.IgnoreQueryFilters()
            .Where(item => item.TenantId == _tenantId && item.ResolvedAt == null)
            .ToListAsync();
        foreach (var match in openMatches)
        {
            match.Resolve(DateTimeOffset.UtcNow);
        }

        var projections = await _dbContext.NormalizedSoftwareVulnerabilityProjections.IgnoreQueryFilters()
            .Where(item => item.TenantId == _tenantId && item.TenantSoftwareId == graph.TenantSoftware.Id)
            .ToListAsync();
        foreach (var projection in projections)
        {
            projection.UpdateProjection(
                projection.SnapshotId,
                projection.BestMatchMethod,
                projection.BestConfidence,
                projection.AffectedInstallCount,
                projection.AffectedDeviceCount,
                projection.AffectedVersionCount,
                projection.FirstSeenAt,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                projection.EvidenceJson
            );
        }

        await _dbContext.SaveChangesAsync();

        var closedCount = await _sut.ReconcileResolvedSoftwareRemediationsAsync(
            _tenantId,
            null,
            CancellationToken.None
        );

        closedCount.Should().Be(1);

        var decision = await _dbContext.RemediationDecisions.IgnoreQueryFilters().FirstAsync();
        decision.ApprovalStatus.Should().Be(DecisionApprovalStatus.Expired);

        var tasks = await _dbContext.PatchingTasks.IgnoreQueryFilters().ToListAsync();
        tasks.Should().OnlyContain(task => task.Status == PatchingTaskStatus.Completed);

    }

    [Fact]
    public async Task ReconcileResolvedSoftwareRemediationsAsync_LeavesDecisionOpen_WhenUnresolvedVulnerabilitiesRemain()
    {
        var graph = await TenantSoftwareGraphFactory.SeedAsync(_dbContext, _tenantId);
        var team = Team.Create(_tenantId, "Platform");
        await _dbContext.Teams.AddAsync(team);

        var devices = await _dbContext.Assets
            .Where(item => item.AssetType == AssetType.Device)
            .ToListAsync();
        foreach (var device in devices)
        {
            device.AssignTeamOwner(team.Id);
        }

        await _dbContext.SaveChangesAsync();

        var softwareAssetId = await _dbContext.NormalizedSoftwareInstallations
            .Where(item => item.TenantSoftwareId == graph.TenantSoftware.Id)
            .Select(item => item.SoftwareAssetId)
            .OrderBy(id => id)
            .FirstAsync();

        var createResult = await _sut.CreateDecisionAsync(
            _tenantId,
            softwareAssetId,
            RemediationOutcome.ApprovedForPatching,
            "Patch it",
            _userId,
            null,
            null,
            CancellationToken.None
        );

        createResult.IsSuccess.Should().BeTrue();

        var closedCount = await _sut.ReconcileResolvedSoftwareRemediationsAsync(
            _tenantId,
            null,
            CancellationToken.None
        );

        closedCount.Should().Be(0);

        var decision = await _dbContext.RemediationDecisions.IgnoreQueryFilters().FirstAsync();
        decision.ApprovalStatus.Should().Be(DecisionApprovalStatus.Approved);

        var tasks = await _dbContext.PatchingTasks.IgnoreQueryFilters().ToListAsync();
        tasks.Should().OnlyContain(task => task.Status != PatchingTaskStatus.Completed);
    }
}
