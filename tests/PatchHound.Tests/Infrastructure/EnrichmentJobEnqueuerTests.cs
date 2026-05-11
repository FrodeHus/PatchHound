using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Tenants;

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
    public async Task EnqueueVulnerabilityJobsAsync_WhenExistingDefenderJobIsCompleted_RefreshesItBackToPending(
        EnrichmentJobStatus existingStatus
    )
    {
        var tenantId = Guid.NewGuid();
        var vulnerability = Vulnerability.Create(
            "MicrosoftDefender",
            "CVE-2026-4242",
            "Test title",
            string.Empty,
            Severity.High,
            cvssScore: 8.0m,
            cvssVector: null,
            publishedDate: null
        );
        var defenderSource = EnrichmentSourceConfiguration.Create(
            EnrichmentSourceCatalog.DefenderSourceKey,
            "Microsoft Defender",
            true,
            apiBaseUrl: TenantSourceCatalog.DefaultDefenderApiBaseUrl
        );
        var tenantDefenderSource = TenantSourceConfiguration.Create(
            tenantId,
            TenantSourceCatalog.DefenderSourceKey,
            "Microsoft Defender",
            true,
            TenantSourceCatalog.DefaultDefenderSchedule,
            "vault/tenant-source",
            TenantSourceCatalog.DefaultDefenderApiBaseUrl,
            "tenant-id",
            "client-id",
            TenantSourceCatalog.DefaultDefenderTokenScope
        );
        var job = EnrichmentJob.Create(
            tenantId,
            EnrichmentSourceCatalog.DefenderSourceKey,
            EnrichmentTargetModel.Vulnerability,
            vulnerability.Id,
            vulnerability.ExternalId,
            100,
            DateTimeOffset.UtcNow.AddHours(-1)
        );
        job.Complete(existingStatus, DateTimeOffset.UtcNow.AddMinutes(-30));

        await _dbContext.Vulnerabilities.AddAsync(vulnerability);
        await _dbContext.EnrichmentSourceConfigurations.AddAsync(defenderSource);
        await _dbContext.TenantSourceConfigurations.AddAsync(tenantDefenderSource);
        await _dbContext.EnrichmentJobs.AddAsync(job);
        await _dbContext.SaveChangesAsync();

        var enqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );

        await enqueuer.EnqueueVulnerabilityJobsAsync(tenantId, [vulnerability.Id], CancellationToken.None);

        var refreshedJob = await _dbContext.EnrichmentJobs.IgnoreQueryFilters().SingleAsync();
        refreshedJob.Status.Should().Be(EnrichmentJobStatus.Pending);
        refreshedJob.NextAttemptAt.Should().BeOnOrAfter(DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task EnqueueVulnerabilityJobsAsync_WhenDefenderJobExistsForAnotherTenant_ReusesGlobalJob()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var vulnerability = Vulnerability.Create(
            "MicrosoftDefender",
            "CVE-2026-4243",
            "Test title",
            string.Empty,
            Severity.High,
            cvssScore: 8.0m,
            cvssVector: null,
            publishedDate: null
        );
        var defenderSource = EnrichmentSourceConfiguration.Create(
            EnrichmentSourceCatalog.DefenderSourceKey,
            "Microsoft Defender",
            true,
            apiBaseUrl: TenantSourceCatalog.DefaultDefenderApiBaseUrl
        );
        var tenantDefenderSourceA = TenantSourceConfiguration.Create(
            tenantA,
            TenantSourceCatalog.DefenderSourceKey,
            "Microsoft Defender",
            true,
            TenantSourceCatalog.DefaultDefenderSchedule,
            "vault/tenant-source",
            TenantSourceCatalog.DefaultDefenderApiBaseUrl,
            "tenant-a",
            "client-a",
            TenantSourceCatalog.DefaultDefenderTokenScope
        );
        var tenantDefenderSourceB = TenantSourceConfiguration.Create(
            tenantB,
            TenantSourceCatalog.DefenderSourceKey,
            "Microsoft Defender",
            true,
            TenantSourceCatalog.DefaultDefenderSchedule,
            "vault/tenant-source",
            TenantSourceCatalog.DefaultDefenderApiBaseUrl,
            "tenant-b",
            "client-b",
            TenantSourceCatalog.DefaultDefenderTokenScope
        );
        var existingJob = EnrichmentJob.Create(
            tenantA,
            EnrichmentSourceCatalog.DefenderSourceKey,
            EnrichmentTargetModel.Vulnerability,
            vulnerability.Id,
            vulnerability.ExternalId,
            100,
            DateTimeOffset.UtcNow.AddHours(-1)
        );
        existingJob.Complete(EnrichmentJobStatus.Succeeded, DateTimeOffset.UtcNow.AddMinutes(-30));

        await _dbContext.Vulnerabilities.AddAsync(vulnerability);
        await _dbContext.EnrichmentSourceConfigurations.AddAsync(defenderSource);
        await _dbContext.TenantSourceConfigurations.AddAsync(tenantDefenderSourceA);
        await _dbContext.TenantSourceConfigurations.AddAsync(tenantDefenderSourceB);
        await _dbContext.EnrichmentJobs.AddAsync(existingJob);
        await _dbContext.SaveChangesAsync();

        var enqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );

        await enqueuer.EnqueueVulnerabilityJobsAsync(tenantB, [vulnerability.Id], CancellationToken.None);

        var jobs = await _dbContext.EnrichmentJobs.IgnoreQueryFilters().ToListAsync();
        jobs.Should().HaveCount(1);
        jobs[0].TenantId.Should().Be(tenantA);
        jobs[0].Status.Should().Be(EnrichmentJobStatus.Pending);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task EnqueueVulnerabilityJobsAsync_WhenDefenderReferenceIsFresh_DoesNotQueueDefenderJob()
    {
        var tenantId = Guid.NewGuid();
        var vulnerability = Vulnerability.Create(
            "MicrosoftDefender",
            "CVE-2026-7777",
            "Test title",
            string.Empty,
            Severity.High,
            cvssScore: 8.0m,
            cvssVector: null,
            publishedDate: null
        );
        var reference = VulnerabilityReference.Create(
            vulnerability.Id,
            "https://api.securitycenter.microsoft.com/api/vulnerabilities/CVE-2026-7777",
            "MicrosoftDefender",
            ["Public Exploit"]
        );
        var assessment = ThreatAssessment.Create(
            vulnerability.Id,
            72m,
            70m,
            75m,
            65m,
            0.5m,
            false,
            true,
            false,
            false,
            false,
            "[]",
            "1"
        );
        assessment.MarkDefenderRefreshed(DateTimeOffset.UtcNow);

        var defenderSource = EnrichmentSourceConfiguration.Create(
            EnrichmentSourceCatalog.DefenderSourceKey,
            "Microsoft Defender",
            true,
            apiBaseUrl: TenantSourceCatalog.DefaultDefenderApiBaseUrl
        );
        var tenantDefenderSource = TenantSourceConfiguration.Create(
            tenantId,
            TenantSourceCatalog.DefenderSourceKey,
            "Microsoft Defender",
            true,
            TenantSourceCatalog.DefaultDefenderSchedule,
            "vault/tenant-source",
            TenantSourceCatalog.DefaultDefenderApiBaseUrl,
            "tenant",
            "client",
            TenantSourceCatalog.DefaultDefenderTokenScope
        );

        await _dbContext.Vulnerabilities.AddAsync(vulnerability);
        await _dbContext.VulnerabilityReferences.AddAsync(reference);
        await _dbContext.ThreatAssessments.AddAsync(assessment);
        await _dbContext.EnrichmentSourceConfigurations.AddAsync(defenderSource);
        await _dbContext.TenantSourceConfigurations.AddAsync(tenantDefenderSource);
        await _dbContext.SaveChangesAsync();

        var enqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );

        await enqueuer.EnqueueVulnerabilityJobsAsync(tenantId, [vulnerability.Id], CancellationToken.None);

        var jobs = await _dbContext.EnrichmentJobs.IgnoreQueryFilters().ToListAsync();
        jobs.Should().BeEmpty();
    }

    [Fact]
    public async Task EnqueueVulnerabilityJobsAsync_WhenDefenderReferenceIsStale_QueuesDefenderJob()
    {
        var tenantId = Guid.NewGuid();
        var vulnerability = Vulnerability.Create(
            "MicrosoftDefender",
            "CVE-2026-8888",
            "Test title",
            string.Empty,
            Severity.High,
            cvssScore: 8.0m,
            cvssVector: null,
            publishedDate: null
        );
        var reference = VulnerabilityReference.Create(
            vulnerability.Id,
            "https://api.securitycenter.microsoft.com/api/vulnerabilities/CVE-2026-8888",
            "MicrosoftDefender",
            ["Public Exploit"]
        );
        var assessment = ThreatAssessment.Create(
            vulnerability.Id,
            72m,
            70m,
            75m,
            65m,
            0.5m,
            false,
            true,
            false,
            false,
            false,
            "[]",
            "1"
        );
        assessment.MarkDefenderRefreshed(
            DateTimeOffset.UtcNow.Subtract(EnrichmentJobEnqueuer.DefaultDefenderRefreshTtl).AddMinutes(-1)
        );

        var defenderSource = EnrichmentSourceConfiguration.Create(
            EnrichmentSourceCatalog.DefenderSourceKey,
            "Microsoft Defender",
            true,
            apiBaseUrl: TenantSourceCatalog.DefaultDefenderApiBaseUrl
        );
        var tenantDefenderSource = TenantSourceConfiguration.Create(
            tenantId,
            TenantSourceCatalog.DefenderSourceKey,
            "Microsoft Defender",
            true,
            TenantSourceCatalog.DefaultDefenderSchedule,
            "vault/tenant-source",
            TenantSourceCatalog.DefaultDefenderApiBaseUrl,
            "tenant",
            "client",
            TenantSourceCatalog.DefaultDefenderTokenScope
        );

        await _dbContext.Vulnerabilities.AddAsync(vulnerability);
        await _dbContext.VulnerabilityReferences.AddAsync(reference);
        await _dbContext.ThreatAssessments.AddAsync(assessment);
        await _dbContext.EnrichmentSourceConfigurations.AddAsync(defenderSource);
        await _dbContext.TenantSourceConfigurations.AddAsync(tenantDefenderSource);
        await _dbContext.SaveChangesAsync();

        var enqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );

        await enqueuer.EnqueueVulnerabilityJobsAsync(tenantId, [vulnerability.Id], CancellationToken.None);

        var jobs = await _dbContext.EnrichmentJobs.IgnoreQueryFilters().ToListAsync();
        jobs.Should().ContainSingle();
        jobs[0].SourceKey.Should().Be(EnrichmentSourceCatalog.DefenderSourceKey);
    }

    [Fact]
    public async Task EnqueueSoftwareSupplyChainJobsAsync_WhenEnabled_QueuesSoftwareJob()
    {
        var tenantId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 3, 30, 12, 0, 0, TimeSpan.Zero);
        var software = SoftwareProduct.Create("contoso", "contoso app", null);

        var source = EnrichmentSourceConfiguration.Create(
            EnrichmentSourceCatalog.SupplyChainSourceKey,
            "Supply Chain Evidence",
            true,
            apiBaseUrl: "https://example.test/catalog.json",
            refreshTtlHours: 24
        );

        await _dbContext.SoftwareProducts.AddAsync(software);
        await _dbContext.EnrichmentSourceConfigurations.AddAsync(source);
        await _dbContext.SaveChangesAsync();

        var enqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );

        await enqueuer.EnqueueSoftwareSupplyChainJobsAsync(tenantId, [software.Id], CancellationToken.None);

        var jobs = await _dbContext.EnrichmentJobs.IgnoreQueryFilters().ToListAsync();
        jobs.Should().ContainSingle();
        jobs[0].SourceKey.Should().Be(EnrichmentSourceCatalog.SupplyChainSourceKey);
        jobs[0].TargetModel.Should().Be(EnrichmentTargetModel.SoftwareAsset);
        jobs[0].TargetId.Should().Be(software.Id);
    }

    [Fact]
    public async Task EnqueueVulnerabilityJobsAsync_WhenNvdSourceIsEnabled_DoesNotQueueNvdJob()
    {
        var tenantId = Guid.NewGuid();
        var vulnerability = Vulnerability.Create(
            "nvd",
            "CVE-2026-9999",
            "Test title",
            string.Empty,
            Severity.Medium,
            cvssScore: null,
            cvssVector: null,
            publishedDate: null
        );
        var nvdSource = EnrichmentSourceConfiguration.Create(
            EnrichmentSourceCatalog.NvdSourceKey,
            "NVD API",
            true,
            apiBaseUrl: EnrichmentSourceCatalog.DefaultNvdApiBaseUrl
        );

        await _dbContext.Vulnerabilities.AddAsync(vulnerability);
        await _dbContext.EnrichmentSourceConfigurations.AddAsync(nvdSource);
        await _dbContext.SaveChangesAsync();

        var enqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );

        await enqueuer.EnqueueVulnerabilityJobsAsync(tenantId, [vulnerability.Id], CancellationToken.None);

        var jobs = await _dbContext.EnrichmentJobs.IgnoreQueryFilters().ToListAsync();
        jobs.Should().BeEmpty("NVD enrichment is handled by NvdCacheBackfillService, not the job queue");
    }
}
