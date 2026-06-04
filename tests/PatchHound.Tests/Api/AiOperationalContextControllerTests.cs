using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Ai;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models.OperationalContext;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Tenants;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class AiOperationalContextControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly IAiOperationalContextService _contextService;
    private readonly PatchHoundDbContext _dbContext;
    private readonly AiOperationalContextController _controller;

    public AiOperationalContextControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );

        _contextService = Substitute.For<IAiOperationalContextService>();

        _controller = new AiOperationalContextController(
            _contextService,
            _tenantContext,
            _dbContext
        );
    }

    private static AiOperationalContextResult BuildResult(string kind) =>
        new()
        {
            Pack = new OperationalContextPack { ContextKind = kind },
            PackJson = "{}",
            TokenEstimate = 123,
            Truncated = true,
            Citations =
            [
                new OperationalContextCitation
                {
                    Key = "c1",
                    EntityType = "Device",
                    EntityId = Guid.NewGuid(),
                    Label = "label",
                    Fact = "fact",
                }
            ],
        };

    [Fact]
    public async Task Preview_remediation_case_returns_pack_for_current_tenant()
    {
        var caseId = Guid.NewGuid();
        _contextService
            .BuildForRemediationCaseAsync(
                _tenantId,
                caseId,
                Arg.Any<AiOperationalContextOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(BuildResult("remediation-case"));

        var action = await _controller.PreviewRemediationCase(caseId, CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<OperationalContextPreviewDto>().Subject;
        dto.ContextKind.Should().Be("remediation-case");
        dto.SubjectId.Should().Be(caseId);
        dto.TokenEstimate.Should().Be(123);
        dto.Truncated.Should().BeTrue();
        dto.Citations.Should().HaveCount(1);
        dto.Citations[0].Key.Should().Be("c1");
    }

    [Fact]
    public async Task Preview_vulnerability_returns_pack_for_current_tenant()
    {
        var vulnId = Guid.NewGuid();
        _contextService
            .BuildForVulnerabilityAsync(
                _tenantId,
                vulnId,
                Arg.Any<AiOperationalContextOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(BuildResult("vulnerability"));

        var action = await _controller.PreviewVulnerability(vulnId, CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<OperationalContextPreviewDto>().Subject;
        dto.ContextKind.Should().Be("vulnerability");
        dto.SubjectId.Should().Be(vulnId);
    }

    [Fact]
    public async Task Preview_uses_tenant_context_not_client_input()
    {
        var caseId = Guid.NewGuid();
        _contextService
            .BuildForRemediationCaseAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<AiOperationalContextOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(BuildResult("remediation-case"));

        await _controller.PreviewRemediationCase(caseId, CancellationToken.None);

        await _contextService.Received(1).BuildForRemediationCaseAsync(
            Arg.Is<Guid>(t => t == _tenantId),
            caseId,
            Arg.Any<AiOperationalContextOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Preview_returns_bad_request_when_no_active_tenant()
    {
        _tenantContext.CurrentTenantId.Returns((Guid?)null);

        var action = await _controller.PreviewRemediationCase(Guid.NewGuid(), CancellationToken.None);

        action.Result.Should().BeOfType<BadRequestObjectResult>();
        await _contextService.DidNotReceiveWithAnyArgs().BuildForRemediationCaseAsync(
            default, default, default!, default);
    }

    [Fact]
    public async Task Preview_remediation_case_returns_not_found_when_subject_missing()
    {
        var caseId = Guid.NewGuid();
        _contextService
            .BuildForRemediationCaseAsync(
                _tenantId,
                caseId,
                Arg.Any<AiOperationalContextOptions>(),
                Arg.Any<CancellationToken>())
            .Returns<AiOperationalContextResult>(_ => throw new InvalidOperationException("Remediation case not found."));

        var action = await _controller.PreviewRemediationCase(caseId, CancellationToken.None);

        action.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Preview_vulnerability_returns_not_found_when_subject_missing()
    {
        var vulnId = Guid.NewGuid();
        _contextService
            .BuildForVulnerabilityAsync(
                _tenantId,
                vulnId,
                Arg.Any<AiOperationalContextOptions>(),
                Arg.Any<CancellationToken>())
            .Returns<AiOperationalContextResult>(_ => throw new InvalidOperationException("Vulnerability not found."));

        var action = await _controller.PreviewVulnerability(vulnId, CancellationToken.None);

        action.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
