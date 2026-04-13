using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Api;

// Phase 1 Task 18: end-to-end verification that canonical entities
// (Device, InstalledSoftware, DeviceRiskScore, SecurityProfile,
// DeviceRule) respect the tenant query filter configured on
// PatchHoundDbContext. Seeds two tenants under a system context, then
// flips the tenant context and asserts that only rows belonging to the
// accessible tenant leak through queries.
public class TenantIsolationEndToEndTests : IDisposable
{
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _sourceSystemId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;

    public TenantIsolationEndToEndTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();

        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new PatchHoundDbContext(
            options,
            TestServiceProviderFactory.Create(_tenantContext)
        );

        UseSystemContext();
        SeedTenant(_tenantA);
        SeedTenant(_tenantB);
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task Device_query_only_returns_rows_for_accessible_tenant()
    {
        UseTenant(_tenantA);

        var devices = await _dbContext.Devices.AsNoTracking().ToListAsync();

        devices.Should().OnlyContain(d => d.TenantId == _tenantA);
        devices.Should().HaveCount(1);
    }

    [Fact]
    public async Task InstalledSoftware_query_only_returns_rows_for_accessible_tenant()
    {
        UseTenant(_tenantB);

        var installs = await _dbContext.InstalledSoftware.AsNoTracking().ToListAsync();

        installs.Should().OnlyContain(i => i.TenantId == _tenantB);
        installs.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeviceRiskScore_query_only_returns_rows_for_accessible_tenant()
    {
        UseTenant(_tenantA);

        var scores = await _dbContext.DeviceRiskScores.AsNoTracking().ToListAsync();

        scores.Should().OnlyContain(s => s.TenantId == _tenantA);
        scores.Should().HaveCount(1);
    }

    [Fact]
    public async Task SecurityProfile_query_only_returns_rows_for_accessible_tenant()
    {
        UseTenant(_tenantB);

        var profiles = await _dbContext.SecurityProfiles.AsNoTracking().ToListAsync();

        profiles.Should().OnlyContain(p => p.TenantId == _tenantB);
        profiles.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeviceRule_query_only_returns_rows_for_accessible_tenant()
    {
        UseTenant(_tenantA);

        var rules = await _dbContext.DeviceRules.AsNoTracking().ToListAsync();

        rules.Should().OnlyContain(r => r.TenantId == _tenantA);
        rules.Should().HaveCount(1);
    }

    [Fact]
    public async Task SystemContext_bypasses_tenant_filter()
    {
        UseSystemContext();

        var devices = await _dbContext.Devices.AsNoTracking().ToListAsync();
        var installs = await _dbContext.InstalledSoftware.AsNoTracking().ToListAsync();
        var profiles = await _dbContext.SecurityProfiles.AsNoTracking().ToListAsync();

        devices.Should().HaveCount(2);
        installs.Should().HaveCount(2);
        profiles.Should().HaveCount(2);
    }

    [Fact]
    public async Task Empty_tenant_scope_returns_no_rows()
    {
        _tenantContext.IsSystemContext.Returns(false);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid>());

        var devices = await _dbContext.Devices.AsNoTracking().ToListAsync();
        var installs = await _dbContext.InstalledSoftware.AsNoTracking().ToListAsync();

        devices.Should().BeEmpty();
        installs.Should().BeEmpty();
    }

    private void UseSystemContext()
    {
        _tenantContext.IsSystemContext.Returns(true);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { _tenantA, _tenantB });
    }

    private void UseTenant(Guid tenantId)
    {
        _tenantContext.IsSystemContext.Returns(false);
        _tenantContext.CurrentTenantId.Returns(tenantId);
        _tenantContext.AccessibleTenantIds.Returns(new List<Guid> { tenantId });
        _tenantContext.HasAccessToTenant(tenantId).Returns(true);
    }

    private void SeedTenant(Guid tenantId)
    {
        var device = Device.Create(
            tenantId,
            _sourceSystemId,
            externalId: $"dev-{tenantId:N}",
            name: $"Device-{tenantId:N}",
            baselineCriticality: Criticality.Medium
        );
        _dbContext.Devices.Add(device);

        var install = InstalledSoftware.Observe(
            tenantId,
            device.Id,
            softwareProductId: Guid.NewGuid(),
            sourceSystemId: _sourceSystemId,
            version: "1.0.0",
            at: DateTimeOffset.UtcNow
        );
        _dbContext.InstalledSoftware.Add(install);

        var riskScore = DeviceRiskScore.Create(
            tenantId,
            device.Id,
            overallScore: 100m,
            maxEpisodeRiskScore: 100m,
            criticalCount: 0,
            highCount: 0,
            mediumCount: 0,
            lowCount: 0,
            openEpisodeCount: 0,
            factorsJson: "{}",
            calculationVersion: "test"
        );
        _dbContext.DeviceRiskScores.Add(riskScore);

        var profile = SecurityProfile.Create(
            tenantId,
            name: $"profile-{tenantId:N}",
            description: null
        );
        _dbContext.SecurityProfiles.Add(profile);

        var rule = DeviceRule.Create(
            tenantId,
            name: $"rule-{tenantId:N}",
            description: null,
            priority: 0,
            filter: new FilterCondition("Name", "eq", "x"),
            operations: new List<AssetRuleOperation>()
        );
        _dbContext.DeviceRules.Add(rule);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
