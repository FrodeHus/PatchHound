using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure.Services;

public class AnalystRecommendationSnapshotLinkTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _analystId = Guid.NewGuid();
    private readonly PatchHoundDbContext _dbContext;

    public AnalystRecommendationSnapshotLinkTests()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.CurrentTenantId.Returns(_tenantId);
        tenantContext.AccessibleTenantIds.Returns([_tenantId]);
        tenantContext.HasAccessToTenant(_tenantId).Returns(true);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(tenantContext)
        );
    }

    private async Task<Guid> SeedCaseAsync()
    {
        var product = SoftwareProduct.Create("Contoso", "Agent", null);
        var remediationCase = RemediationCase.Create(_tenantId, product.Id);
        await _dbContext.AddRangeAsync(product, remediationCase);
        await _dbContext.SaveChangesAsync();
        return remediationCase.Id;
    }

    private async Task<Guid> SeedSnapshotAsync(Guid tenantId, Guid caseId)
    {
        var snapshot = RecommendationContextSnapshot.Create(
            tenantId, caseId, "{\"contextKind\":\"RemediationCase\"}", "hash", "[]", _analystId);
        await _dbContext.RecommendationContextSnapshots.AddAsync(snapshot);
        await _dbContext.SaveChangesAsync();
        return snapshot.Id;
    }

    private AnalystRecommendationService Sut() =>
        new(_dbContext, new RemediationWorkflowService(_dbContext));

    [Fact]
    public async Task Links_snapshot_belonging_to_same_tenant_and_case()
    {
        var caseId = await SeedCaseAsync();
        var snapshotId = await SeedSnapshotAsync(_tenantId, caseId);

        var result = await Sut().AddRecommendationForCaseAsync(
            _tenantId, caseId, RemediationOutcome.ApprovedForPatching, "Patch now.",
            _analystId, vulnerabilityId: null, priorityOverride: null,
            contextSnapshotId: snapshotId, ct: CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.ContextSnapshotId.Should().Be(snapshotId);
    }

    [Fact]
    public async Task Does_not_link_snapshot_from_a_different_case()
    {
        var caseId = await SeedCaseAsync();
        var otherProduct = SoftwareProduct.Create("Other", "Thing", null);
        var otherCase = RemediationCase.Create(_tenantId, otherProduct.Id);
        await _dbContext.AddRangeAsync(otherProduct, otherCase);
        await _dbContext.SaveChangesAsync();
        var foreignSnapshotId = await SeedSnapshotAsync(_tenantId, otherCase.Id);

        var result = await Sut().AddRecommendationForCaseAsync(
            _tenantId, caseId, RemediationOutcome.ApprovedForPatching, "Patch now.",
            _analystId, vulnerabilityId: null, priorityOverride: null,
            contextSnapshotId: foreignSnapshotId, ct: CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.ContextSnapshotId.Should().BeNull();
    }

    [Fact]
    public async Task Does_not_link_snapshot_from_a_different_tenant()
    {
        var caseId = await SeedCaseAsync();
        var foreignSnapshotId = await SeedSnapshotAsync(Guid.NewGuid(), caseId);

        var result = await Sut().AddRecommendationForCaseAsync(
            _tenantId, caseId, RemediationOutcome.ApprovedForPatching, "Patch now.",
            _analystId, vulnerabilityId: null, priorityOverride: null,
            contextSnapshotId: foreignSnapshotId, ct: CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.ContextSnapshotId.Should().BeNull();
    }

    public void Dispose() => _dbContext.Dispose();
}
