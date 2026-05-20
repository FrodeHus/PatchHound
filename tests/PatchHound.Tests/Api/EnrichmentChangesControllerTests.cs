using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models;
using PatchHound.Api.Models.EnrichmentChanges;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class EnrichmentChangesControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly EnrichmentChangesController _controller;

    public EnrichmentChangesControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        _tenantContext.IsSystemContext.Returns(false);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );
        _controller = new EnrichmentChangesController(_dbContext, _tenantContext);
    }

    [Fact]
    public async Task List_ReturnsGlobalAndActiveTenantRowsOnly()
    {
        var entityId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var changedAt = DateTimeOffset.Parse("2026-05-20T08:15:00Z");
        var global = CreateChange(
            EnrichmentChangeScope.Global,
            tenantId: null,
            entityId,
            "nvd",
            "cvss.score",
            "Global score",
            changedAt.AddMinutes(-1)
        );
        var activeTenant = CreateChange(
            EnrichmentChangeScope.Tenant,
            _tenantId,
            entityId,
            "defender",
            "exploit.available",
            "Tenant exploit flag",
            changedAt
        );
        var otherTenant = CreateChange(
            EnrichmentChangeScope.Tenant,
            otherTenantId,
            entityId,
            "defender",
            "hidden",
            "Other tenant",
            changedAt.AddMinutes(1)
        );

        _dbContext.EnrichmentChangeLogs.AddRange(global, activeTenant, otherTenant);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(
            new EnrichmentChangeFilterQuery(
                EntityType: "Vulnerability",
                EntityId: entityId,
                SourceKey: null,
                FieldPath: null,
                FromDate: null,
                ToDate: null
            ),
            new PaginationQuery(1, 50),
            CancellationToken.None
        );

        var payload = action.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<PagedResponse<EnrichmentChangeDto>>().Subject;

        payload.TotalCount.Should().Be(2);
        payload.Items.Select(item => item.DisplayName)
            .Should()
            .Equal("Tenant exploit flag", "Global score");
        payload.Items.Select(item => item.SourceDisplayName)
            .Should()
            .Equal("Microsoft Defender", "NVD API");
    }

    [Fact]
    public async Task List_ReturnsBadRequestWhenActiveTenantMissing()
    {
        _tenantContext.CurrentTenantId.Returns((Guid?)null);

        var action = await _controller.List(
            new EnrichmentChangeFilterQuery(
                EntityType: "Vulnerability",
                EntityId: Guid.NewGuid(),
                SourceKey: null,
                FieldPath: null,
                FromDate: null,
                ToDate: null
            ),
            new PaginationQuery(),
            CancellationToken.None
        );

        var badRequest = action.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeOfType<ProblemDetails>()
            .Which.Title.Should().Be("No active tenant is selected.");
    }

    [Fact]
    public async Task List_AppliesOptionalSourceAndFieldFilters()
    {
        var entityId = Guid.NewGuid();
        var changedAt = DateTimeOffset.Parse("2026-05-20T08:15:00Z");
        _dbContext.EnrichmentChangeLogs.AddRange(
            CreateChange(
                EnrichmentChangeScope.Global,
                tenantId: null,
                entityId,
                "nvd",
                "cvss.score",
                "Matching",
                changedAt
            ),
            CreateChange(
                EnrichmentChangeScope.Global,
                tenantId: null,
                entityId,
                "defender",
                "cvss.score",
                "Wrong source",
                changedAt
            ),
            CreateChange(
                EnrichmentChangeScope.Global,
                tenantId: null,
                entityId,
                "nvd",
                "summary",
                "Wrong field",
                changedAt
            )
        );
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(
            new EnrichmentChangeFilterQuery(
                EntityType: "Vulnerability",
                EntityId: entityId,
                SourceKey: "NVD",
                FieldPath: "cvss.score",
                FromDate: changedAt.AddMinutes(-1),
                ToDate: changedAt.AddMinutes(1)
            ),
            new PaginationQuery(),
            CancellationToken.None
        );

        var payload = action.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<PagedResponse<EnrichmentChangeDto>>().Subject;

        payload.Items.Should().ContainSingle()
            .Which.DisplayName.Should().Be("Matching");
    }

    [Fact]
    public async Task List_ParsesOldAndNewJsonValues()
    {
        var entityId = Guid.NewGuid();
        var change = CreateChange(
            EnrichmentChangeScope.Global,
            tenantId: null,
            entityId,
            "supply-chain",
            "evidence",
            "Evidence",
            DateTimeOffset.Parse("2026-05-20T08:15:00Z"),
            oldValueJson: """{"score":7.5,"flags":["known-exploited"],"verified":true}""",
            newValueJson: """"patched"""",
            EnrichmentChangeValueKind.Object
        );

        await _dbContext.EnrichmentChangeLogs.AddAsync(change);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(
            new EnrichmentChangeFilterQuery(
                EntityType: "Vulnerability",
                EntityId: entityId,
                SourceKey: null,
                FieldPath: null,
                FromDate: null,
                ToDate: null
            ),
            new PaginationQuery(),
            CancellationToken.None
        );

        var item = action.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<PagedResponse<EnrichmentChangeDto>>().Subject
            .Items.Should().ContainSingle().Subject;

        item.SourceDisplayName.Should().Be("Supply Chain Evidence");
        item.NewValue.Should().Be("patched");
        var oldValue = item.OldValue.Should()
            .BeAssignableTo<IReadOnlyDictionary<string, object?>>().Subject;
        oldValue["score"].Should().Be(7.5m);
        oldValue["verified"].Should().Be(true);
        oldValue["flags"].Should()
            .BeAssignableTo<IReadOnlyList<object?>>().Subject
            .Should().ContainSingle().Which.Should().Be("known-exploited");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private static EnrichmentChangeLog CreateChange(
        EnrichmentChangeScope scope,
        Guid? tenantId,
        Guid entityId,
        string sourceKey,
        string fieldPath,
        string displayName,
        DateTimeOffset changedAt,
        string? oldValueJson = null,
        string? newValueJson = null,
        EnrichmentChangeValueKind valueKind = EnrichmentChangeValueKind.String
    ) =>
        EnrichmentChangeLog.Create(
            scope,
            tenantId,
            entityType: "Vulnerability",
            entityId,
            sourceKey,
            enrichmentRunId: null,
            enrichmentJobId: null,
            fieldPath,
            displayName,
            oldValueJson,
            newValueJson ?? """"new"""",
            valueKind,
            changedAt
        );
}
