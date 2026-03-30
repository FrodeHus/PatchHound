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

    [Fact]
    public async Task EnqueueVulnerabilityJobsAsync_WhenJobAlreadyExistsForAnotherTenant_ReusesGlobalJob()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-4243",
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
        var existingJob = EnrichmentJob.Create(
            tenantA,
            "nvd",
            EnrichmentTargetModel.Vulnerability,
            definition.Id,
            definition.ExternalId,
            100,
            DateTimeOffset.UtcNow.AddHours(-1)
        );
        existingJob.Complete(EnrichmentJobStatus.Succeeded, DateTimeOffset.UtcNow.AddMinutes(-30));

        await _dbContext.VulnerabilityDefinitions.AddAsync(definition);
        await _dbContext.EnrichmentSourceConfigurations.AddAsync(source);
        await _dbContext.EnrichmentJobs.AddAsync(existingJob);
        await _dbContext.SaveChangesAsync();

        var enqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );

        await enqueuer.EnqueueVulnerabilityJobsAsync(tenantB, [definition.Id], CancellationToken.None);

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
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-7777",
            "Test title",
            string.Empty,
            Severity.High,
            "MicrosoftDefender",
            cvssScore: 8.0m,
            cvssVector: null,
            publishedDate: null
        );
        var reference = VulnerabilityDefinitionReference.Create(
            definition.Id,
            "https://api.securitycenter.microsoft.com/api/vulnerabilities/CVE-2026-7777",
            "MicrosoftDefender",
            ["Public Exploit"]
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
        var assessment = VulnerabilityThreatAssessment.Create(
            definition.Id,
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
            VulnerabilityThreatAssessmentService.CalculationVersion
        );
        assessment.MarkDefenderRefreshed(DateTimeOffset.UtcNow);

        await _dbContext.VulnerabilityDefinitions.AddAsync(definition);
        await _dbContext.VulnerabilityDefinitionReferences.AddAsync(reference);
        await _dbContext.EnrichmentSourceConfigurations.AddAsync(defenderSource);
        await _dbContext.TenantSourceConfigurations.AddAsync(tenantDefenderSource);
        await _dbContext.VulnerabilityThreatAssessments.AddAsync(assessment);
        await _dbContext.SaveChangesAsync();

        var enqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );

        await enqueuer.EnqueueVulnerabilityJobsAsync(tenantId, [definition.Id], CancellationToken.None);

        var jobs = await _dbContext.EnrichmentJobs.IgnoreQueryFilters().ToListAsync();
        jobs.Should().BeEmpty();
    }

    [Fact]
    public async Task EnqueueVulnerabilityJobsAsync_WhenDefenderReferenceIsStale_QueuesDefenderJob()
    {
        var tenantId = Guid.NewGuid();
        var definition = VulnerabilityDefinition.Create(
            "CVE-2026-8888",
            "Test title",
            string.Empty,
            Severity.High,
            "MicrosoftDefender",
            cvssScore: 8.0m,
            cvssVector: null,
            publishedDate: null
        );
        var reference = VulnerabilityDefinitionReference.Create(
            definition.Id,
            "https://api.securitycenter.microsoft.com/api/vulnerabilities/CVE-2026-8888",
            "MicrosoftDefender",
            ["Public Exploit"]
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
        var assessment = VulnerabilityThreatAssessment.Create(
            definition.Id,
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
            VulnerabilityThreatAssessmentService.CalculationVersion
        );
        assessment.MarkDefenderRefreshed(
            DateTimeOffset.UtcNow.Subtract(EnrichmentJobEnqueuer.DefaultDefenderRefreshTtl).AddMinutes(-1)
        );

        await _dbContext.VulnerabilityDefinitions.AddAsync(definition);
        await _dbContext.VulnerabilityDefinitionReferences.AddAsync(reference);
        await _dbContext.EnrichmentSourceConfigurations.AddAsync(defenderSource);
        await _dbContext.TenantSourceConfigurations.AddAsync(tenantDefenderSource);
        await _dbContext.VulnerabilityThreatAssessments.AddAsync(assessment);
        await _dbContext.SaveChangesAsync();

        var enqueuer = new EnrichmentJobEnqueuer(
            _dbContext,
            Substitute.For<ILogger<EnrichmentJobEnqueuer>>()
        );

        await enqueuer.EnqueueVulnerabilityJobsAsync(tenantId, [definition.Id], CancellationToken.None);

        var jobs = await _dbContext.EnrichmentJobs.IgnoreQueryFilters().ToListAsync();
        jobs.Should().ContainSingle();
        jobs[0].SourceKey.Should().Be(EnrichmentSourceCatalog.DefenderSourceKey);
    }

    [Fact]
    public async Task EnqueueSoftwareSupplyChainJobsAsync_WhenEnabled_QueuesSoftwareJob()
    {
        var tenantId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 3, 30, 12, 0, 0, TimeSpan.Zero);
        var software = NormalizedSoftware.Create(
            "contoso app",
            "contoso",
            "contoso|app",
            null,
            SoftwareNormalizationMethod.Heuristic,
            SoftwareNormalizationConfidence.High,
            timestamp
        );

        var source = EnrichmentSourceConfiguration.Create(
            EnrichmentSourceCatalog.SupplyChainSourceKey,
            "Supply Chain Evidence",
            true,
            apiBaseUrl: "https://example.test/catalog.json",
            refreshTtlHours: 24
        );

        await _dbContext.NormalizedSoftware.AddAsync(software);
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
}
