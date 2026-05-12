using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Api.Models.Dashboard;
using PatchHound.Api.Services;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.Infrastructure;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public sealed class DashboardControllerSummaryAggregationTests : IDisposable
{
    private readonly Guid _tenantId;
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly DashboardController _controller;

    public DashboardControllerSummaryAggregationTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId = Guid.NewGuid());
        _tenantContext.AccessibleTenantIds.Returns([_tenantId]);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );

        _controller = new DashboardController(
            _dbContext,
            new DashboardQueryService(_dbContext, Substitute.For<IRiskChangeBriefAiSummaryService>()),
            _tenantContext,
            new TenantSnapshotResolver(_dbContext)
        );
    }

    [Fact]
    public async Task GetSummary_VulnerabilityAgeBuckets_CountUniqueVulnerabilities()
    {
        var seed = await CanonicalSeed.PlantAsync(_dbContext, _tenantId);
        _dbContext.DeviceVulnerabilityExposures.Add(DeviceVulnerabilityExposure.Observe(
            _tenantId,
            seed.DeviceB.Id,
            seed.ExposureA.VulnerabilityId,
            seed.ProductB.Id,
            seed.InstallB.Id,
            "2.0.0",
            ExposureMatchSource.Product,
            DateTimeOffset.UtcNow));
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetSummary(new DashboardFilterQuery(), CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<DashboardSummaryDto>().Subject;
        var newestBucket = dto.VulnerabilityAgeBuckets.Should()
            .Contain(bucket => bucket.Bucket == "0-7 days")
            .Subject;
        newestBucket.Count.Should().Be(2);
        newestBucket.Critical.Should().Be(1);
        newestBucket.High.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_TopCriticalVulnerabilities_ExcludesAlternateMitigationVulnerabilities()
    {
        var seed = await CanonicalSeed.PlantAsync(_dbContext, _tenantId);
        var remediationCase = RemediationCase.Create(_tenantId, seed.ProductA.Id);
        var decision = RemediationDecision.Create(
            _tenantId,
            remediationCase.Id,
            RemediationOutcome.AlternateMitigation,
            "Mitigated by security control",
            Guid.NewGuid(),
            DecisionApprovalStatus.Approved);

        _dbContext.RemediationCases.Add(remediationCase);
        _dbContext.RemediationDecisions.Add(decision);
        await _dbContext.SaveChangesAsync();

        _dbContext.ApprovedVulnerabilityRemediations.Add(ApprovedVulnerabilityRemediation.Create(
            _tenantId,
            seed.ExposureA.VulnerabilityId,
            remediationCase.Id,
            decision.Id,
            decision.Outcome,
            decision.ApprovedAt!.Value));
        await _dbContext.SaveChangesAsync();

        var action = await _controller.GetSummary(new DashboardFilterQuery(), CancellationToken.None);

        var ok = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<DashboardSummaryDto>().Subject;
        dto.TopCriticalVulnerabilities.Should().NotContain(item => item.Id == seed.ExposureA.VulnerabilityId);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
