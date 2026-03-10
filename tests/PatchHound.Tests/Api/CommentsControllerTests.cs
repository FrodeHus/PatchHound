using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Comments;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;

namespace PatchHound.Tests.Api;

public class CommentsControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly CommentsController _controller;

    public CommentsControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);
        _tenantContext.CurrentUserId.Returns(_userId);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(options, BuildServiceProvider(_tenantContext));
        _controller = new CommentsController(_dbContext, _tenantContext);
    }

    [Fact]
    public async Task Create_VulnerabilityComment_StoresTenantVulnerabilityEntityType()
    {
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-1000",
            "Example vulnerability",
            "Description",
            Severity.High,
            "NVD"
        );
        var tenantVulnerability = TenantVulnerability.Create(
            _tenantId,
            definition.Id,
            VulnerabilityStatus.Open,
            DateTimeOffset.UtcNow
        );

        await _dbContext.AddRangeAsync(definition, tenantVulnerability);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Create(
            "vulnerabilities",
            tenantVulnerability.Id,
            new CreateCommentRequest("Needs triage"),
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<CreatedResult>().Subject;
        var dto = result.Value.Should().BeOfType<CommentDto>().Subject;

        dto.EntityType.Should().Be("TenantVulnerability");
        dto.EntityId.Should().Be(tenantVulnerability.Id);

        var stored = await _dbContext.Comments.SingleAsync();
        stored.EntityType.Should().Be("TenantVulnerability");
        stored.EntityId.Should().Be(tenantVulnerability.Id);
    }

    [Fact]
    public async Task List_VulnerabilityComments_ReturnsTenantVulnerabilityComments()
    {
        var tenantVulnerabilityId = Guid.NewGuid();
        var comment = Comment.Create(
            _tenantId,
            "TenantVulnerability",
            tenantVulnerabilityId,
            _userId,
            "Investigating"
        );

        await _dbContext.Comments.AddAsync(comment);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.List(
            "vulnerabilities",
            tenantVulnerabilityId,
            CancellationToken.None
        );

        var result = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var items = result.Value.Should().BeOfType<List<CommentDto>>().Subject;

        items.Should().ContainSingle();
        items[0].EntityType.Should().Be("TenantVulnerability");
        items[0].EntityId.Should().Be(tenantVulnerabilityId);
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
