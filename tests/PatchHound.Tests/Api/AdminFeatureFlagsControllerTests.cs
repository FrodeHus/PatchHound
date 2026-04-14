using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.FeatureManagement;
using NSubstitute;
using PatchHound.Api.Controllers;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

public class AdminFeatureFlagsControllerTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly IFeatureManager _featureManager;
    private readonly AdminFeatureFlagsController _controller;

    public AdminFeatureFlagsControllerTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.CurrentUserId.Returns(_userId);

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );

        _featureManager = Substitute.For<IFeatureManager>();
        // Default: all flags enabled in the mock
        _featureManager.IsEnabledAsync(Arg.Any<string>()).Returns(true);

        _controller = new AdminFeatureFlagsController(_dbContext, _featureManager);
    }

    // ── GetFlags ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetFlags_ReturnsOneEntryPerRegisteredFlag()
    {
        var result = await _controller.GetFlags(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var flags = ok.Value.Should().BeAssignableTo<IReadOnlyList<AdminFeatureFlagDto>>().Subject;

        flags.Select(f => f.FlagName)
            .Should().BeEquivalentTo(FeatureFlags.Metadata.Keys);
    }

    [Fact]
    public async Task GetFlags_ReflectsFeatureManagerState()
    {
        _featureManager.IsEnabledAsync(FeatureFlags.Workflows).Returns(false);
        _featureManager.IsEnabledAsync(FeatureFlags.AuthenticatedScans).Returns(true);

        var result = await _controller.GetFlags(CancellationToken.None);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var flags = ok.Value.Should().BeAssignableTo<IReadOnlyList<AdminFeatureFlagDto>>().Subject;

        flags.Single(f => f.FlagName == FeatureFlags.Workflows).IsEnabled.Should().BeFalse();
        flags.Single(f => f.FlagName == FeatureFlags.AuthenticatedScans).IsEnabled.Should().BeTrue();
    }

    // ── GetOverrides ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOverrides_ReturnsEmpty_WhenNoneExist()
    {
        var result = await _controller.GetOverrides(null, null, CancellationToken.None);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var overrides = ok.Value.Should().BeAssignableTo<IReadOnlyList<FeatureFlagOverrideDto>>().Subject;
        overrides.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverrides_FiltersByTenantId()
    {
        var otherTenantId = Guid.NewGuid();
        await _dbContext.FeatureFlagOverrides.AddRangeAsync(
            FeatureFlagOverride.CreateTenantOverride(FeatureFlags.Workflows, _tenantId, true),
            FeatureFlagOverride.CreateTenantOverride(FeatureFlags.Workflows, otherTenantId, false)
        );
        await _dbContext.SaveChangesAsync();

        var result = await _controller.GetOverrides(_tenantId, null, CancellationToken.None);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var overrides = ok.Value.Should().BeAssignableTo<IReadOnlyList<FeatureFlagOverrideDto>>().Subject;

        overrides.Should().HaveCount(1);
        overrides[0].TenantId.Should().Be(_tenantId);
    }

    // ── CreateOrUpdateOverride ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateOverride_PersistsTenantOverride()
    {
        var request = new UpsertFeatureFlagOverrideRequest(
            FlagName: FeatureFlags.Workflows,
            TenantId: _tenantId,
            UserId: null,
            IsEnabled: false,
            ExpiresAt: null
        );

        var result = await _controller.CreateOrUpdateOverride(request, CancellationToken.None);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<FeatureFlagOverrideDto>().Subject;

        dto.FlagName.Should().Be(FeatureFlags.Workflows);
        dto.TenantId.Should().Be(_tenantId);
        dto.IsEnabled.Should().BeFalse();

        var stored = await _dbContext.FeatureFlagOverrides.IgnoreQueryFilters().SingleAsync();
        stored.TenantId.Should().Be(_tenantId);
    }

    [Fact]
    public async Task CreateOverride_UpsertsExistingOverride()
    {
        // First create
        var create = new UpsertFeatureFlagOverrideRequest(FeatureFlags.Workflows, _tenantId, null, false, null);
        await _controller.CreateOrUpdateOverride(create, CancellationToken.None);

        // Now upsert with isEnabled = true
        var upsert = new UpsertFeatureFlagOverrideRequest(FeatureFlags.Workflows, _tenantId, null, true, null);
        var result = await _controller.CreateOrUpdateOverride(upsert, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<FeatureFlagOverrideDto>().Subject;
        dto.IsEnabled.Should().BeTrue();

        // Should still be exactly one override
        var count = await _dbContext.FeatureFlagOverrides.IgnoreQueryFilters().CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task CreateOverride_ReturnsBadRequest_ForUnknownFlag()
    {
        var request = new UpsertFeatureFlagOverrideRequest("UnknownFlag", _tenantId, null, true, null);
        var result = await _controller.CreateOrUpdateOverride(request, CancellationToken.None);
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateOverride_ReturnsBadRequest_WhenBothTenantAndUserProvided()
    {
        var request = new UpsertFeatureFlagOverrideRequest(FeatureFlags.Workflows, _tenantId, _userId, true, null);
        var result = await _controller.CreateOrUpdateOverride(request, CancellationToken.None);
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateOverride_ReturnsBadRequest_WhenNeitherTenantNorUserProvided()
    {
        var request = new UpsertFeatureFlagOverrideRequest(FeatureFlags.Workflows, null, null, true, null);
        var result = await _controller.CreateOrUpdateOverride(request, CancellationToken.None);
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── DeleteOverride ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteOverride_RemovesExistingOverride()
    {
        var entity = FeatureFlagOverride.CreateTenantOverride(FeatureFlags.Workflows, _tenantId, true);
        await _dbContext.FeatureFlagOverrides.AddAsync(entity);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.DeleteOverride(entity.Id, CancellationToken.None);
        result.Should().BeOfType<NoContentResult>();

        var count = await _dbContext.FeatureFlagOverrides.IgnoreQueryFilters().CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task DeleteOverride_ReturnsNotFound_ForMissingId()
    {
        var result = await _controller.DeleteOverride(Guid.NewGuid(), CancellationToken.None);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    public void Dispose() => _dbContext.Dispose();
}
