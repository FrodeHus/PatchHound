using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.FeatureManagement;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.FeatureFlags;

/// <summary>
/// Resolves feature flag state with the following precedence (highest first):
///   1. User-level override (non-expired)
///   2. Tenant-level override (non-expired)
///   3. Global default from IConfiguration ("FeatureManagement" section)
/// </summary>
public class DatabaseFeatureDefinitionProvider : IFeatureDefinitionProvider
{
    private readonly IDbContextFactory<PatchHoundDbContext> _dbContextFactory;
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;

    public DatabaseFeatureDefinitionProvider(
        IDbContextFactory<PatchHoundDbContext> dbContextFactory,
        ITenantContext tenantContext,
        IConfiguration configuration
    )
    {
        _dbContextFactory = dbContextFactory;
        _tenantContext = tenantContext;
        _configuration = configuration;
    }

    public async Task<FeatureDefinition> GetFeatureDefinitionAsync(string featureName)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = _tenantContext.CurrentUserId != Guid.Empty
            ? _tenantContext.CurrentUserId
            : (Guid?)null;
        var tenantId = _tenantContext.CurrentTenantId;

        await using var db = await _dbContextFactory.CreateDbContextAsync();

        // 1. User-level override
        if (userId.HasValue)
        {
            var userOverride = await db.FeatureFlagOverrides
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(o =>
                    o.FlagName == featureName &&
                    o.UserId == userId.Value &&
                    (o.ExpiresAt == null || o.ExpiresAt > now))
                .FirstOrDefaultAsync();

            if (userOverride is not null)
                return BuildDefinition(featureName, userOverride.IsEnabled);
        }

        // 2. Tenant-level override
        if (tenantId.HasValue)
        {
            var tenantOverride = await db.FeatureFlagOverrides
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(o =>
                    o.FlagName == featureName &&
                    o.TenantId == tenantId.Value &&
                    (o.ExpiresAt == null || o.ExpiresAt > now))
                .FirstOrDefaultAsync();

            if (tenantOverride is not null)
                return BuildDefinition(featureName, tenantOverride.IsEnabled);
        }

        // 3. Global config default
        var configValue = _configuration[$"FeatureManagement:{featureName}"];
        var isEnabled = string.Equals(configValue, "true", StringComparison.OrdinalIgnoreCase);
        return BuildDefinition(featureName, isEnabled);
    }

    public async IAsyncEnumerable<FeatureDefinition> GetAllFeatureDefinitionsAsync()
    {
        // Return definitions for all known registered flags
        foreach (var flagName in Core.Common.FeatureFlags.Metadata.Keys)
        {
            yield return await GetFeatureDefinitionAsync(flagName);
        }
    }

    private static FeatureDefinition BuildDefinition(string featureName, bool isEnabled)
    {
        return new FeatureDefinition
        {
            Name = featureName,
            EnabledFor = isEnabled
                ? [new FeatureFilterConfiguration { Name = "AlwaysOn" }]
                : [],
        };
    }
}
