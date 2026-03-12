using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Tests.Infrastructure;

public class EnrichmentJobEnqueuerTests : IDisposable
{
    private readonly PatchHoundDbContext _dbContext;

    public EnrichmentJobEnqueuerTests()
    {
        var options = new DbContextOptionsBuilder<PatchHoundDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new PatchHoundDbContext(options, new ServiceCollection().BuildServiceProvider());
    }

    [Theory]
    [InlineData(EnrichmentJobStatus.Succeeded)]
    [InlineData(EnrichmentJobStatus.Skipped)]
    public async Task EnqueueVulnerabilityJobsAsync_WhenExistingJobIsCompleted_RefreshesItBackToPending(
        EnrichmentJobStatus existingStatus
    )
    {
        var tenantId = Guid.NewGuid();
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-4242",
            "Test title",
            string.Empty,
            Severity.High,
            "MicrosoftDefender",
            cvssScore: 8.0m,
            cvssVector: null,
            publishedDate: null
        );
        var source = EnrichmentSourceConfiguration.Create(
            "nvd",
            "NVD API",
            true,
            "system/enrichment-sources/nvd",
            "https://services.nvd.nist.gov"
        );
        var job = EnrichmentJob.Create(
            tenantId,
            "nvd",
            EnrichmentTargetModel.Vulnerability,
            definition.Id,
            definition.ExternalId,
            100,
            DateTimeOffset.UtcNow.AddHours(-1)
        );
        job.Complete(existingStatus, DateTimeOffset.UtcNow.AddMinutes(-30));

        await _dbContext.VulnerabilityDefinitions.AddAsync(definition);
        await _dbContext.EnrichmentSourceConfigurations.AddAsync(source);
        await _dbContext.EnrichmentJobs.AddAsync(job);
        await _dbContext.SaveChangesAsync();

        var enqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );

        await enqueuer.EnqueueVulnerabilityJobsAsync(tenantId, [definition.Id], CancellationToken.None);

        var refreshedJob = await _dbContext.EnrichmentJobs.IgnoreQueryFilters().SingleAsync();
        refreshedJob.Status.Should().Be(EnrichmentJobStatus.Pending);
        refreshedJob.NextAttemptAt.Should().BeOnOrAfter(DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
