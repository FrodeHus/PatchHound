using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;

namespace PatchHound.Tests.Infrastructure;

public class EnrichmentChangeLogQueryFilterTests
{
    [Fact]
    public async Task Query_filter_shows_global_and_current_tenant_rows_only()
    {
        using var db = TestDbContextFactory.CreateTenantContext(out var tenantId);
        var otherTenantId = Guid.NewGuid();
        var changedAt = DateTimeOffset.UtcNow;

        var global = CreateChange(
            EnrichmentChangeScope.Global,
            tenantId: null,
            "global-visible",
            changedAt
        );
        var currentTenant = CreateChange(
            EnrichmentChangeScope.Tenant,
            tenantId,
            "tenant-visible",
            changedAt
        );
        var otherTenant = CreateChange(
            EnrichmentChangeScope.Tenant,
            otherTenantId,
            "tenant-hidden",
            changedAt
        );
        var malformedGlobal = CreateChange(
            EnrichmentChangeScope.Global,
            tenantId: null,
            "global-with-tenant-hidden",
            changedAt
        );
        typeof(EnrichmentChangeLog)
            .GetProperty(nameof(EnrichmentChangeLog.TenantId))!
            .SetValue(malformedGlobal, tenantId);

        db.EnrichmentChangeLogs.AddRange(global, currentTenant, otherTenant, malformedGlobal);
        await db.SaveChangesAsync();

        var visibleNames = await db.EnrichmentChangeLogs
            .Select(change => change.DisplayName)
            .OrderBy(name => name)
            .ToListAsync();

        visibleNames.Should().Equal("global-visible", "tenant-visible");
    }

    [Fact]
    public void Factory_rejects_scope_tenant_mismatches()
    {
        var tenantId = Guid.NewGuid();
        var changedAt = DateTimeOffset.UtcNow;

        var globalWithTenant = () => CreateChange(
            EnrichmentChangeScope.Global,
            tenantId,
            "invalid-global",
            changedAt
        );
        var tenantWithoutTenantId = () => CreateChange(
            EnrichmentChangeScope.Tenant,
            tenantId: null,
            "invalid-tenant",
            changedAt
        );

        globalWithTenant.Should().Throw<ArgumentException>();
        tenantWithoutTenantId.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Model_contains_scope_tenant_check_constraint()
    {
        using var db = TestDbContextFactory.CreateSystemContext();

        var entityType = db.GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(EnrichmentChangeLog));
        var checkConstraint = entityType
            ?.GetCheckConstraints()
            .SingleOrDefault(constraint =>
                constraint.Name == "CK_EnrichmentChangeLog_Scope_TenantId"
            );

        checkConstraint.Should().NotBeNull();
        checkConstraint!.Sql.Should().Be(
            "(\"Scope\" = 'Global' AND \"TenantId\" IS NULL) OR (\"Scope\" = 'Tenant' AND \"TenantId\" IS NOT NULL)"
        );
    }

    private static EnrichmentChangeLog CreateChange(
        EnrichmentChangeScope scope,
        Guid? tenantId,
        string displayName,
        DateTimeOffset changedAt
    ) =>
        EnrichmentChangeLog.Create(
            scope,
            tenantId,
            entityType: "Vulnerability",
            entityId: Guid.NewGuid(),
            sourceKey: "defender",
            enrichmentRunId: null,
            enrichmentJobId: null,
            fieldPath: "description",
            displayName,
            oldValueJson: null,
            newValueJson: "\"value\"",
            EnrichmentChangeValueKind.String,
            changedAt
        );
}
