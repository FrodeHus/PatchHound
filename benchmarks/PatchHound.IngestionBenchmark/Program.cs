using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Services.Bulk;
using PatchHound.Infrastructure.Services.Inventory;
using PatchHound.IngestionBenchmark;
using Testcontainers.PostgreSql;

var opts = BenchmarkOptions.Parse(args);

Console.WriteLine("PatchHound ingestion benchmark");
Console.WriteLine("==============================");
Console.WriteLine($"  tenants            : {opts.TenantCount}");
Console.WriteLine($"  devices/tenant     : {opts.DevicesPerTenant}");
Console.WriteLine($"  vulns/device       : {opts.VulnsPerDevice}");
Console.WriteLine($"  software/device    : {opts.SoftwarePerDevice}");
Console.WriteLine($"  runs               : {opts.Runs}");
Console.WriteLine($"  total devices      : {opts.TotalDevices}");
Console.WriteLine($"  total staged exps  : {opts.TotalStagedExposures}");
Console.WriteLine();

Console.Write("Starting Postgres container ... ");
var containerSw = Stopwatch.StartNew();
await using var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
await container.StartAsync();
containerSw.Stop();
Console.WriteLine($"ready in {containerSw.ElapsedMilliseconds:N0} ms");

var connectionString = container.GetConnectionString();
var dbOptions = new DbContextOptionsBuilder<PatchHoundDbContext>()
    .UseNpgsql(connectionString)
    .Options;

PatchHoundDbContext CreateDb(Guid tenantId)
{
    var services = new ServiceCollection();
    var tenantContext = new BenchmarkTenantContext(tenantId);
    services.AddSingleton<ITenantContext>(tenantContext);
    var sp = services.BuildServiceProvider();
    return new PatchHoundDbContext(dbOptions, sp);
}

Console.Write("Applying EF migrations ... ");
var migrateSw = Stopwatch.StartNew();
await using (var db = CreateDb(Guid.Empty))
{
    await db.Database.MigrateAsync();
}
migrateSw.Stop();
Console.WriteLine($"done in {migrateSw.ElapsedMilliseconds:N0} ms");
Console.WriteLine();

var tenantIds = new Guid[opts.TenantCount];
for (var t = 0; t < opts.TenantCount; t++)
{
    // Deterministic tenant ids so re-runs hit the same rows and exercise UPSERT.
    tenantIds[t] = new Guid($"00000001-0000-0000-0000-{t:D12}");
}

// Ensure source system exists once.
await using (var db = CreateDb(tenantIds[0]))
{
    await BenchmarkSeeder.EnsureSourceSystemAsync(db, CancellationToken.None);
}

for (var runIndex = 0; runIndex < opts.Runs; runIndex++)
{
    var seedTotal = TimeSpan.Zero;
    var mergeTotal = TimeSpan.Zero;
    var processStagedTotal = TimeSpan.Zero;
    var derivationTotal = TimeSpan.Zero;
    var episodeTotal = TimeSpan.Zero;
    var projectionTotal = TimeSpan.Zero;

    foreach (var tenantId in tenantIds)
    {
        await using var db = CreateDb(tenantId);

        var runId = Guid.NewGuid();
        var observedAt = DateTimeOffset.UtcNow;

        // ── Seed (excluded from totals) ──
        var seedSw = Stopwatch.StartNew();
        BenchmarkSeeder.Seed(db, tenantId, runId, observedAt, opts);
        await db.SaveChangesAsync();
        seedSw.Stop();
        seedTotal += seedSw.Elapsed;

        // ── Wire services (same shape as the Task 9 E2E test) ──
        var bulkDeviceMergeWriter = new PostgresBulkDeviceMergeWriter(db);
        var bulkExposureWriter = new PostgresBulkExposureWriter(db);
        var bulkVulnRefWriter = new PostgresBulkVulnerabilityReferenceWriter(db);
        var bulkSoftwareProjectionWriter = new PostgresBulkSoftwareProjectionWriter(db);

        var softwareResolver = new SoftwareProductResolver(db);
        var stagedDeviceMergeService = new StagedDeviceMergeService(
            db, softwareResolver, bulkDeviceMergeWriter);
        var vulnerabilityResolver = new VulnerabilityResolver(
            db, NullLogger<VulnerabilityResolver>.Instance);
        var exposureDerivationService = new ExposureDerivationService(
            db, NullLogger<ExposureDerivationService>.Instance, bulkExposureWriter);
        var exposureEpisodeService = new ExposureEpisodeService(db);
        var exposureAssessmentService = new ExposureAssessmentService(
            db, new EnvironmentalSeverityCalculator());
        var normalizedSoftwareProjectionService = new NormalizedSoftwareProjectionService(
            bulkSoftwareProjectionWriter);

        var enrichmentEnqueuer = new EnrichmentJobEnqueuer(
            db, NullLogger<EnrichmentJobEnqueuer>.Instance);
        var inMemoryIngestionBulk = new InMemoryIngestionBulkWriter(db);
        var leaseManager = new IngestionLeaseManager(
            db, inMemoryIngestionBulk, NullLogger<IngestionLeaseManager>.Instance);
        var checkpointWriter = new IngestionCheckpointWriter(db);
        var stagingPipeline = new IngestionStagingPipeline(
            db, enrichmentEnqueuer, leaseManager, checkpointWriter);
        var snapshotLifecycle = new IngestionSnapshotLifecycle(db, inMemoryIngestionBulk);

        var ingestion = new IngestionService(
            db,
            Enumerable.Empty<IIngestionSource>(),
            enrichmentEnqueuer,
            stagedDeviceMergeService,
            new NoopStagedCloudApplicationMergeService(),
            new NoopDeviceRuleEvaluationService(),
            exposureDerivationService,
            exposureEpisodeService,
            exposureAssessmentService,
            new RiskScoreService(db, NullLogger<RiskScoreService>.Instance),
            vulnerabilityResolver,
            normalizedSoftwareProjectionService,
            remediationDecisionService: null,
            vulnerabilityAssessmentJobService: null,
            notificationService: null,
            leaseManager,
            checkpointWriter,
            stagingPipeline,
            snapshotLifecycle,
            inMemoryIngestionBulk,
            bulkExposureWriter,
            bulkVulnRefWriter,
            materializedViewRefreshService: null,
            NullLogger<IngestionService>.Instance);

        // ── Stage 1: device merge (ProcessStagedAssetsAsync drives StagedDeviceMergeService) ──
        var mergeSw = Stopwatch.StartNew();
        await ingestion.ProcessStagedAssetsAsync(
            runId, tenantId, BenchmarkSeeder.SourceKey, snapshotId: null, CancellationToken.None);
        mergeSw.Stop();
        mergeTotal += mergeSw.Elapsed;

        // ── Stage 2: vuln + exposure bulk path ──
        var processSw = Stopwatch.StartNew();
        await ingestion.ProcessStagedResultsAsync(
            runId, tenantId, BenchmarkSeeder.SourceKey, snapshotId: null,
            "Benchmark Synthetic Source", CancellationToken.None);
        processSw.Stop();
        processStagedTotal += processSw.Elapsed;

        // ── Stage 3: CTE-derived canonical exposures ──
        var deriveSw = Stopwatch.StartNew();
        await exposureDerivationService.DeriveForTenantAsync(
            tenantId, observedAt, runId, CancellationToken.None);
        await db.SaveChangesAsync();
        deriveSw.Stop();
        derivationTotal += deriveSw.Elapsed;

        // ── Stage 4: run-scoped episode sync ──
        var episodeSw = Stopwatch.StartNew();
        await exposureEpisodeService.SyncEpisodesForTenantAsync(
            tenantId, runId, observedAt, CancellationToken.None);
        await db.SaveChangesAsync();
        episodeSw.Stop();
        episodeTotal += episodeSw.Elapsed;

        // ── Stage 5: normalized software projection ──
        var projectSw = Stopwatch.StartNew();
        await normalizedSoftwareProjectionService.SyncTenantAsync(
            tenantId, snapshotId: null, CancellationToken.None);
        projectSw.Stop();
        projectionTotal += projectSw.Elapsed;
    }

    var total = mergeTotal + processStagedTotal + derivationTotal + episodeTotal + projectionTotal;
    var timings = new StageTimings(
        SeedExcluded: seedTotal,
        Merge: mergeTotal,
        ProcessStaged: processStagedTotal,
        ExposureDerivation: derivationTotal,
        EpisodeSync: episodeTotal,
        SoftwareProjection: projectionTotal,
        Total: total);
    timings.PrintTo(Console.Out, runIndex, opts);
    Console.WriteLine();
}

Console.WriteLine("Done. Disposing container ...");

// ───────── helpers ─────────

internal sealed class BenchmarkTenantContext : ITenantContext
{
    private readonly Guid _tenantId;
    public BenchmarkTenantContext(Guid tenantId) { _tenantId = tenantId; }
    public Guid? CurrentTenantId => _tenantId;
    public IReadOnlyList<Guid> AccessibleTenantIds => new[] { _tenantId };
    public Guid CurrentUserId => Guid.Empty;
    public bool IsSystemContext => false;
    public bool IsInternalUser => false;
    public PatchHound.Core.Enums.UserAccessScope CurrentAccessScope =>
        PatchHound.Core.Enums.UserAccessScope.Customer;
    public bool HasAccessToTenant(Guid tenantId) => tenantId == _tenantId;
    public IReadOnlyList<string> GetRolesForTenant(Guid tenantId) => Array.Empty<string>();
}

internal sealed class NoopStagedCloudApplicationMergeService : IStagedCloudApplicationMergeService
{
    public Task<StagedCloudApplicationMergeSummary> MergeAsync(Guid ingestionRunId, Guid tenantId, CancellationToken ct)
        => Task.FromResult(new StagedCloudApplicationMergeSummary(0, 0, 0));
}

internal sealed class NoopDeviceRuleEvaluationService : IDeviceRuleEvaluationService
{
    public Task EvaluateRulesAsync(Guid tenantId, CancellationToken ct) => Task.CompletedTask;
    public Task EvaluateCriticalityForDeviceAsync(Guid tenantId, Guid deviceId, CancellationToken ct) => Task.CompletedTask;
    public Task<DeviceRulePreviewResult> PreviewFilterAsync(
        Guid tenantId, PatchHound.Core.Models.FilterNode filter, CancellationToken ct)
        // Note: DeviceRulePreviewResult / DevicePreviewItem live in PatchHound.Core.Interfaces.
        => Task.FromResult(new DeviceRulePreviewResult(0, new List<DevicePreviewItem>()));
}
