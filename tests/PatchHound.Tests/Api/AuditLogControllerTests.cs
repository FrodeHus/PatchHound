using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Audit;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Tests.Api;

public class AuditLogControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;

    public AuditLogControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantId });
        _tenantContext.CurrentUserId.Returns(_userId);
        _tenantContext.HasAccessToTenant(Arg.Any<Guid>()).Returns(callInfo =>
            new List<Guid> { _tenantId }.Contains(callInfo.Arg<Guid>()));

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(options, BuildServiceProvider(_tenantContext));
    }

    [Fact]
    public async Task List_EnrichesEntriesWithActorAndEntityLabel()
    {
        var user = User.Create("auditor@example.com", "Audrey Admin", Guid.NewGuid().ToString());
        var entry = AuditLogEntry.Create(
            _tenantId,
            "AssetSecurityProfile",
            Guid.NewGuid(),
            AuditAction.Updated,
            """{"Name":"Internet-facing server"}""",
            """{"Name":"Internet-facing server","AvailabilityRequirement":"High"}""",
            user.Id
        );

        await _dbContext.Users.AddAsync(user);
        await _dbContext.AuditLogEntries.AddAsync(entry);
        await _dbContext.SaveChangesAsync();

        var controller = new AuditLogController(_dbContext, _tenantContext);

        var action = await controller.List(
            new AuditLogFilterQuery(EntityType: "AssetSecurityProfile"),
            new PaginationQuery(1, 10),
            CancellationToken.None
        );

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<PagedResponse<AuditLogDto>>().Subject;
        var item = payload.Items.Should().ContainSingle().Subject;

        item.UserDisplayName.Should().Be("Audrey Admin");
        item.EntityLabel.Should().Be("Internet-facing server");
    }

    [Fact]
    public async Task List_ReturnsPaginationMetadata()
    {
        var user = User.Create("auditor@example.com", "Audrey Admin", Guid.NewGuid().ToString());
        await _dbContext.Users.AddAsync(user);

        for (var i = 0; i < 3; i++)
        {
            await _dbContext.AuditLogEntries.AddAsync(
                AuditLogEntry.Create(
                    _tenantId,
                    "Tenant",
                    Guid.NewGuid(),
                    AuditAction.Created,
                    null,
                    $$"""{"Name":"Tenant {{i}}"}""",
                    user.Id
                )
            );
        }

        await _dbContext.SaveChangesAsync();

        var controller = new AuditLogController(_dbContext, _tenantContext);

        var action = await controller.List(
            new AuditLogFilterQuery(),
            new PaginationQuery(2, 2),
            CancellationToken.None
        );

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<PagedResponse<AuditLogDto>>().Subject;

        payload.Page.Should().Be(2);
        payload.PageSize.Should().Be(2);
        payload.TotalCount.Should().Be(3);
        payload.TotalPages.Should().Be(2);
        payload.Items.Should().HaveCount(1);
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
