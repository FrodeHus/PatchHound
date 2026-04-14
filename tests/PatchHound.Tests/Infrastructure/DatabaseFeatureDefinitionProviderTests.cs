using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using PatchHound.Core.Common;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.FeatureFlags;
using PatchHound.Tests.TestData;

namespace PatchHound.Tests.Infrastructure;

/// <summary>
/// Tests the resolution precedence of DatabaseFeatureDefinitionProvider:
///   1. User-level override beats tenant-level and global config.
///   2. Tenant-level override beats global config.
///   3. Global config is the final fallback.
///   4. Expired overrides are ignored.
/// </summary>
public class DatabaseFeatureDefinitionProviderTests : IDisposable
{
    private static readonly string FlagName = FeatureFlags.Workflows;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly PatchHoundDbContext _dbContext;
    private readonly string _databaseName = Guid.NewGuid().ToString();

    public DatabaseFeatureDefinitionProviderTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.CurrentTenantId.Returns(_tenantId);
        _tenantContext.CurrentUserId.Returns(_userId);

        _dbContext = BuildDbContext();
    }

    private PatchHoundDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .Options;
        return new PatchHoundDbContext(options, TestServiceProviderFactory.Create(_tenantContext));
    }

    private DatabaseFeatureDefinitionProvider BuildProvider(bool globalDefault)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"FeatureManagement:{FlagName}"] = globalDefault ? "true" : "false",
            })
            .Build();

        var factory = new FakeDbContextFactory(BuildDbContext);
        return new DatabaseFeatureDefinitionProvider(factory, _tenantContext, config);
    }

    [Fact]
    public async Task GlobalConfig_IsReturnedWhenNoOverridesExist()
    {
        var provider = BuildProvider(globalDefault: true);
        var definition = await provider.GetFeatureDefinitionAsync(FlagName);
        definition.EnabledFor.Should().NotBeEmpty("global config has flag enabled");
    }

    [Fact]
    public async Task GlobalConfig_Disabled_IsReturnedWhenNoOverridesExist()
    {
        var provider = BuildProvider(globalDefault: false);
        var definition = await provider.GetFeatureDefinitionAsync(FlagName);
        definition.EnabledFor.Should().BeEmpty("global config has flag disabled");
    }

    [Fact]
    public async Task TenantOverride_WinsOverGlobalConfig()
    {
        // Global says disabled, tenant says enabled
        await _dbContext.FeatureFlagOverrides.AddAsync(
            FeatureFlagOverride.CreateTenantOverride(FlagName, _tenantId, isEnabled: true));
        await _dbContext.SaveChangesAsync();

        var provider = BuildProvider(globalDefault: false);
        var definition = await provider.GetFeatureDefinitionAsync(FlagName);
        definition.EnabledFor.Should().NotBeEmpty("tenant override enables the flag");
    }

    [Fact]
    public async Task UserOverride_WinsOverTenantOverrideAndGlobalConfig()
    {
        // Tenant says disabled, user says enabled
        await _dbContext.FeatureFlagOverrides.AddRangeAsync(
            FeatureFlagOverride.CreateTenantOverride(FlagName, _tenantId, isEnabled: false),
            FeatureFlagOverride.CreateUserOverride(FlagName, _userId, isEnabled: true)
        );
        await _dbContext.SaveChangesAsync();

        var provider = BuildProvider(globalDefault: false);
        var definition = await provider.GetFeatureDefinitionAsync(FlagName);
        definition.EnabledFor.Should().NotBeEmpty("user override takes highest precedence");
    }

    [Fact]
    public async Task ExpiredOverride_IsBypassed_FallsBackToGlobal()
    {
        var expired = FeatureFlagOverride.CreateTenantOverride(
            FlagName, _tenantId, isEnabled: true,
            expiresAt: DateTimeOffset.UtcNow.AddHours(-1)); // already expired

        await _dbContext.FeatureFlagOverrides.AddAsync(expired);
        await _dbContext.SaveChangesAsync();

        var provider = BuildProvider(globalDefault: false);
        var definition = await provider.GetFeatureDefinitionAsync(FlagName);
        definition.EnabledFor.Should().BeEmpty("expired override must not apply");
    }

    [Fact]
    public async Task GetAllFeatureDefinitions_ReturnsOneDefinitionPerRegisteredFlag()
    {
        var provider = BuildProvider(globalDefault: true);
        var definitions = new List<Microsoft.FeatureManagement.FeatureDefinition>();
        await foreach (var def in provider.GetAllFeatureDefinitionsAsync())
            definitions.Add(def);

        definitions.Select(d => d.Name)
            .Should().BeEquivalentTo(FeatureFlags.Metadata.Keys);
    }

    public void Dispose() => _dbContext.Dispose();

    // Minimal IDbContextFactory<PatchHoundDbContext> that creates a fresh context
    // sharing the same in-memory database, so each `await using` disposal is safe.
    private sealed class FakeDbContextFactory(
        Func<PatchHoundDbContext> factory) : Microsoft.EntityFrameworkCore.IDbContextFactory<PatchHoundDbContext>
    {
        public PatchHoundDbContext CreateDbContext() => factory();
    }
}
