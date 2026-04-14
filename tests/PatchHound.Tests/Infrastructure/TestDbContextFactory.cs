using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure;

/// <summary>
/// Minimal helper that builds an in-memory <see cref="PatchHoundDbContext"/> wired to a
/// no-tenant <see cref="ITenantContext"/> stub (<c>IsSystemContext = true</c>). Used by
/// Phase 1 service tests that exercise global (non-tenant) canonical entities such as
/// <see cref="PatchHound.Core.Entities.SoftwareProduct"/> and
/// <see cref="PatchHound.Core.Entities.SoftwareAlias"/>.
/// </summary>
internal static class TestDbContextFactory
{
    public static async Task<PatchHoundDbContext> CreateAsync()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns((Guid?)null);
        tenantContext.AccessibleTenantIds.Returns(Array.Empty<Guid>());
        tenantContext.IsSystemContext.Returns(true);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(tenantContext)
        );

        await db.Database.EnsureCreatedAsync();
        return db;
    }

    /// <summary>
    /// Creates a system-context (IsSystemContext = true) in-memory DbContext.
    /// No tenant filter is applied — all rows are visible.
    /// </summary>
    public static PatchHoundDbContext CreateSystemContext()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns((Guid?)null);
        tenantContext.AccessibleTenantIds.Returns(Array.Empty<Guid>());
        tenantContext.IsSystemContext.Returns(true);

        return Build(tenantContext);
    }

    /// <summary>
    /// Creates an in-memory DbContext scoped to a single new tenant.
    /// <paramref name="tenantId"/> is set to the generated tenant id.
    /// </summary>
    public static PatchHoundDbContext CreateTenantContext(out Guid tenantId)
    {
        tenantId = Guid.NewGuid();
        var tid = tenantId;

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns((Guid?)tid);
        tenantContext.AccessibleTenantIds.Returns(new[] { tid });
        tenantContext.IsSystemContext.Returns(false);

        return Build(tenantContext);
    }

    private static PatchHoundDbContext Build(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(tenantContext)
        );

        db.Database.EnsureCreated();
        return db;
    }
}
