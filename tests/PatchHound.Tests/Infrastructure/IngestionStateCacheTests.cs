using PatchHound.Infrastructure.Services;

namespace PatchHound.Tests.Infrastructure;

public class IngestionStateCacheTests
{
    [Fact]
    public void IsAvailable_WhenNoRedis_ReturnsFalse()
    {
        var cache = new IngestionStateCache(null);
        Assert.False(cache.IsAvailable);
    }

    [Fact]
    public async Task PreWarmMethods_WhenNoRedis_DoNotThrow()
    {
        var cache = new IngestionStateCache(null);
        cache.SetScope(Guid.NewGuid(), Guid.NewGuid());

        // Phase 2: legacy PreWarmTenantVulnerabilitiesAsync, PreWarmDefinitionsAsync,
        // PreWarmProjectionsAsync, PreWarmOpenEpisodesAsync, PreWarmLatestEpisodeNumbersAsync,
        // and PreWarmAssessmentsAsync have been removed. Replaced by PreWarmVulnerabilitiesAsync.
        await cache.PreWarmVulnerabilitiesAsync([], CancellationToken.None);
        await cache.PreWarmAssetsAsync([], CancellationToken.None);
        await cache.CleanupAsync(CancellationToken.None);
    }
}
