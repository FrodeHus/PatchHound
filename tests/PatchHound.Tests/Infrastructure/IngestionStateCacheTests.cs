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

        await cache.PreWarmTenantVulnerabilitiesAsync([], CancellationToken.None);
        await cache.PreWarmDefinitionsAsync([], CancellationToken.None);
        await cache.PreWarmAssetsAsync([], CancellationToken.None);
        await cache.PreWarmProjectionsAsync([], CancellationToken.None);
        await cache.PreWarmOpenEpisodesAsync([], CancellationToken.None);
        await cache.PreWarmLatestEpisodeNumbersAsync([], CancellationToken.None);
        await cache.PreWarmAssessmentsAsync([], CancellationToken.None);
        await cache.CleanupAsync(CancellationToken.None);
    }
}
