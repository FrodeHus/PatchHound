using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure;

public class AuditInterceptorTests : IDisposable
{
    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public AuditInterceptorTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentUserId.Returns(_userId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        _tenantContext.CurrentTenantId.Returns(_tenantId);

        var interceptor = new AuditSaveChangesInterceptor(_tenantContext);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );
    }

    [Fact]
    public async Task SavingNewEntity_CreatesAuditEntry_WithCreatedAction()
    {
        var tenant = Tenant.Create("Test Org", "entra-123");
        _dbContext.Tenants.Add(tenant);
        await _dbContext.SaveChangesAsync();

        var auditEntries = await _dbContext.AuditLogEntries.IgnoreQueryFilters().ToListAsync();

        auditEntries.Should().ContainSingle();
        auditEntries[0].Action.Should().Be(AuditAction.Created);
        auditEntries[0].EntityType.Should().Be("Tenant");
        auditEntries[0].EntityId.Should().Be(tenant.Id);
        auditEntries[0].UserId.Should().Be(_userId);
        auditEntries[0].NewValues.Should().NotBeNullOrEmpty();
        auditEntries[0].OldValues.Should().BeNull();
    }

    [Fact]
    public async Task ModifyingEntity_CreatesAuditEntry_WithUpdatedAction()
    {
        var tenant = Tenant.Create("Test Org", "entra-123");
        _dbContext.Tenants.Add(tenant);
        await _dbContext.SaveChangesAsync();

        tenant.UpdateName("Updated Org");
        await _dbContext.SaveChangesAsync();

        var auditEntries = await _dbContext
            .AuditLogEntries.IgnoreQueryFilters()
            .Where(a => a.Action == AuditAction.Updated)
            .ToListAsync();

        auditEntries.Should().ContainSingle();
        auditEntries[0].OldValues.Should().NotBeNullOrEmpty();
        auditEntries[0].NewValues.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DeletingEntity_CreatesAuditEntry_WithDeletedAction()
    {
        var tenant = Tenant.Create("Test Org", "entra-123");
        _dbContext.Tenants.Add(tenant);
        await _dbContext.SaveChangesAsync();

        _dbContext.Tenants.Remove(tenant);
        await _dbContext.SaveChangesAsync();

        var auditEntries = await _dbContext
            .AuditLogEntries.IgnoreQueryFilters()
            .Where(a => a.Action == AuditAction.Deleted)
            .ToListAsync();

        auditEntries.Should().ContainSingle();
        auditEntries[0].OldValues.Should().NotBeNullOrEmpty();
        auditEntries[0].NewValues.Should().BeNull();
    }

    [Fact]
    public async Task AuditLogEntries_AreNotAudited_Themselves()
    {
        var tenant = Tenant.Create("Test Org", "entra-123");
        _dbContext.Tenants.Add(tenant);
        await _dbContext.SaveChangesAsync();

        // Should have 1 audit entry for Tenant, not additional entries for the AuditLogEntry itself
        var auditEntries = await _dbContext.AuditLogEntries.IgnoreQueryFilters().ToListAsync();

        auditEntries.Should().ContainSingle();
        auditEntries[0].EntityType.Should().Be("Tenant");
    }

    [Fact]
    public async Task DeletingIngestionCleanupEntities_DoesNotCreateAuditEntries()
    {
        var run = IngestionRun.Start(_tenantId, "test-source", DateTimeOffset.UtcNow);
        var checkpoint = IngestionCheckpoint.Start(
            run.Id,
            _tenantId,
            "test-source",
            "asset-staging",
            DateTimeOffset.UtcNow
        );
        var stagedAsset = StagedAsset.Create(
            run.Id,
            _tenantId,
            "test-source",
            "device-1",
            "Device 1",
            AssetType.Device,
            "{}",
            DateTimeOffset.UtcNow
        );

        _dbContext.IngestionRuns.Add(run);
        _dbContext.IngestionCheckpoints.Add(checkpoint);
        _dbContext.StagedAssets.Add(stagedAsset);
        await _dbContext.SaveChangesAsync();

        _dbContext.AuditLogEntries.RemoveRange(_dbContext.AuditLogEntries.IgnoreQueryFilters());
        await _dbContext.SaveChangesAsync();

        _dbContext.IngestionRuns.Remove(run);
        _dbContext.IngestionCheckpoints.Remove(checkpoint);
        _dbContext.StagedAssets.Remove(stagedAsset);
        await _dbContext.SaveChangesAsync();

        var auditEntries = await _dbContext.AuditLogEntries.IgnoreQueryFilters().ToListAsync();
        auditEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdatingOnlyBookkeepingFields_DoesNotCreateAuditEntry()
    {
        var team = Team.Create(_tenantId, "Dynamic team");
        team.SetDynamic(true);
        var rule = TeamMembershipRule.Create(
            _tenantId,
            team.Id,
            new PatchHound.Core.Models.FilterGroup("and", [])
        );

        _dbContext.Teams.Add(team);
        _dbContext.TeamMembershipRules.Add(rule);
        await _dbContext.SaveChangesAsync();

        _dbContext.AuditLogEntries.RemoveRange(_dbContext.AuditLogEntries.IgnoreQueryFilters());
        await _dbContext.SaveChangesAsync();

        rule.RecordExecution(12);
        await _dbContext.SaveChangesAsync();

        var auditEntries = await _dbContext.AuditLogEntries.IgnoreQueryFilters().ToListAsync();
        auditEntries.Should().BeEmpty();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
