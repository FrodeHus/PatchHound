using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Vigil.Core.Entities;
using Vigil.Core.Enums;
using Vigil.Core.Interfaces;
using Vigil.Infrastructure.Data;

namespace Vigil.Tests.Infrastructure;

public class AuditInterceptorTests : IDisposable
{
    private readonly VigilDbContext _dbContext;
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

        var options = new DbContextOptionsBuilder<VigilDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        _dbContext = new VigilDbContext(options, BuildServiceProvider(_tenantContext));
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

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private static IServiceProvider BuildServiceProvider(ITenantContext tenantContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);
        return services.BuildServiceProvider();
    }
}
