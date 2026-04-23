using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models;
using PatchHound.Api.Models.Software;
using PatchHound.Api.Services;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Services;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class SoftwareControllerOwnershipTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly SoftwareController _controller;

    public SoftwareControllerOwnershipTests()
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

        var aiConfigurationResolver = Substitute.For<ITenantAiConfigurationResolver>();

        _controller = new SoftwareController(
            _dbContext,
            new TenantAiTextGenerationService([], aiConfigurationResolver),
            new SoftwareDescriptionJobService(_dbContext),
            aiConfigurationResolver,
            Substitute.For<ITenantAiResearchService>(),
            new RemediationTaskQueryService(_dbContext),
            new CycloneDxSupplyChainImportService(_dbContext),
            _tenantContext
        );
    }

    [Fact]
    public async Task Get_ReturnsOwnerFields()
    {
        var ownerTeam = Team.Create(_tenantId, "Platform Engineering");
        var product = SoftwareProduct.Create("Contoso Agent", "Contoso", null);
        var tenantSoftware = SoftwareTenantRecord.Create(
            _tenantId,
            null,
            product.Id,
            DateTimeOffset.UtcNow.AddDays(-5),
            DateTimeOffset.UtcNow.AddDays(-1)
        );
        tenantSoftware.AssignOwnerTeamFromRule(ownerTeam.Id, Guid.NewGuid());

        await _dbContext.AddRangeAsync(ownerTeam, product, tenantSoftware);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.Get(tenantSoftware.Id, CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<TenantSoftwareDetailDto>().Subject;
        dto.OwnerTeamId.Should().Be(ownerTeam.Id);
        dto.OwnerTeamName.Should().Be("Platform Engineering");
        dto.OwnerTeamManagedByRule.Should().BeTrue();
    }

    [Fact]
    public async Task AssignOwner_UpdatesTenantSoftwareAndActiveWorkflow()
    {
        var previousTeam = Team.Create(_tenantId, "Old Owners");
        var nextTeam = Team.Create(_tenantId, "New Owners");
        var product = SoftwareProduct.Create("Contoso Agent", "Contoso", null);
        var tenantSoftware = SoftwareTenantRecord.Create(
            _tenantId,
            null,
            product.Id,
            DateTimeOffset.UtcNow.AddDays(-5),
            DateTimeOffset.UtcNow.AddDays(-1)
        );
        tenantSoftware.AssignOwnerTeamFromRule(previousTeam.Id, Guid.NewGuid());

        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        var workflow = RemediationWorkflow.Create(_tenantId, remediationCase.Id, previousTeam.Id);

        await _dbContext.AddRangeAsync(previousTeam, nextTeam, product, tenantSoftware, remediationCase, workflow);
        await _dbContext.SaveChangesAsync();

        var action = await _controller.AssignOwner(
            tenantSoftware.Id,
            new AssignTenantSoftwareOwnerRequest(nextTeam.Id),
            CancellationToken.None
        );

        action.Should().BeOfType<NoContentResult>();

        var storedSoftware = await _dbContext.SoftwareTenantRecords.SingleAsync(item => item.Id == tenantSoftware.Id);
        storedSoftware.OwnerTeamId.Should().Be(nextTeam.Id);
        storedSoftware.OwnerTeamRuleId.Should().BeNull();

        var storedWorkflow = await _dbContext.RemediationWorkflows.SingleAsync(item => item.Id == workflow.Id);
        storedWorkflow.SoftwareOwnerTeamId.Should().Be(nextTeam.Id);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
