using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Infrastructure.AuthenticatedScans;

public class AuthenticatedScanIngestionService
{
    private const string SourceKey = "authenticated-scan";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PatchHoundDbContext _dbContext;
    private readonly AuthenticatedScanOutputValidator _validator;
    private readonly IStagedDeviceMergeService _stagedDeviceMergeService;
    private readonly NormalizedSoftwareProjectionService _projectionService;
    private readonly IIngestionBulkWriter _bulkWriter;

    [ActivatorUtilitiesConstructor]
    internal AuthenticatedScanIngestionService(
        PatchHoundDbContext dbContext,
        AuthenticatedScanOutputValidator validator,
        IStagedDeviceMergeService stagedDeviceMergeService,
        NormalizedSoftwareProjectionService projectionService,
        IIngestionBulkWriter bulkWriter)
    {
        _dbContext = dbContext;
        _validator = validator;
        _stagedDeviceMergeService = stagedDeviceMergeService;
        _projectionService = projectionService;
        _bulkWriter = bulkWriter;
    }

    public async Task ProcessJobResultAsync(Guid scanJobId, string rawStdout, string rawStderr, CancellationToken ct)
    {
        var job = await _dbContext.ScanJobs.FirstOrDefaultAsync(j => j.Id == scanJobId, ct)
            ?? throw new InvalidOperationException($"ScanJob {scanJobId} not found");

        var result = ScanJobResult.Create(scanJobId, rawStdout, rawStderr, string.Empty);
        await _dbContext.ScanJobResults.AddAsync(result, ct);

        var validation = _validator.Validate(rawStdout);

        if (validation.FatalError)
        {
            job.CompleteFailed(ScanJobStatuses.Failed, $"Validation: {validation.FatalErrorMessage}", DateTimeOffset.UtcNow);
            await SaveValidationIssues(scanJobId, validation.Issues, ct);
            await _dbContext.SaveChangesAsync(ct);
            return;
        }

        await SaveValidationIssues(scanJobId, validation.Issues, ct);

        if (validation.ValidEntries.Count == 0)
        {
            job.CompleteSucceeded(rawStdout.Length, rawStderr.Length, 0, DateTimeOffset.UtcNow);
            await _dbContext.SaveChangesAsync(ct);
            return;
        }

        // Stage software records and device-software links into the ingestion pipeline
        var ingestionRunId = Guid.NewGuid();
        var stagedAt = DateTimeOffset.UtcNow;
        var deviceExternalId = await GetDeviceExternalId(job.DeviceId, ct);

        var stagedSoftware = new List<StagedDevice>();
        var softwareLinks = new List<StagedDeviceSoftwareInstallation>();

        foreach (var entry in validation.ValidEntries)
        {
            var softwareExternalId = BuildSoftwareExternalId(job.TenantId, entry);
            var metadata = JsonSerializer.Serialize(new
            {
                name = entry.Name,
                vendor = entry.Vendor,
                version = entry.Version,
                category = (string?)null,
                installPath = entry.InstallPath,
            }, JsonOptions);

            var ingestionAsset = new
            {
                ExternalId = softwareExternalId,
                Name = entry.Name,
                AssetType = AssetType.Software,
                Description = (string?)null,
                Metadata = metadata,
            };

            stagedSoftware.Add(StagedDevice.Create(
                ingestionRunId,
                job.TenantId,
                SourceKey,
                softwareExternalId,
                entry.Name,
                AssetType.Software,
                JsonSerializer.Serialize(ingestionAsset, JsonOptions),
                stagedAt
            ));

            softwareLinks.Add(StagedDeviceSoftwareInstallation.Create(
                ingestionRunId,
                job.TenantId,
                SourceKey,
                deviceExternalId,
                softwareExternalId,
                stagedAt,
                JsonSerializer.Serialize(new
                {
                    DeviceExternalId = deviceExternalId,
                    SoftwareExternalId = softwareExternalId,
                    ObservedAt = stagedAt,
                }, JsonOptions),
                stagedAt
            ));
        }

        await _dbContext.StagedDevices.AddRangeAsync(stagedSoftware, ct);
        await _dbContext.StagedDeviceSoftwareInstallations.AddRangeAsync(softwareLinks, ct);
        await _dbContext.SaveChangesAsync(ct);
        _dbContext.ChangeTracker.Clear();

        // Merge + resolve + project through the canonical pipeline
        await _stagedDeviceMergeService.MergeAsync(ingestionRunId, job.TenantId, ct);
        await _projectionService.SyncTenantAsync(job.TenantId, null, ct);

        // Delete staged rows now that merge is complete. No IngestionRun entity is created for
        // authenticated-scan jobs, so CleanupExpiredIngestionArtifactsAsync would never find these
        // rows and they would persist indefinitely without explicit cleanup here.
        await _bulkWriter.ClearStagedDataForRunAsync(ingestionRunId, ct);

        job = await _dbContext.ScanJobs.FirstAsync(j => j.Id == scanJobId, ct);
        job.CompleteSucceeded(rawStdout.Length, rawStderr.Length, validation.ValidEntries.Count, DateTimeOffset.UtcNow);
        await _dbContext.SaveChangesAsync(ct);
    }

    private async Task<string> GetDeviceExternalId(Guid deviceId, CancellationToken ct)
    {
        var device = await _dbContext.Devices.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        return device?.ExternalId ?? deviceId.ToString();
    }

    private static string BuildSoftwareExternalId(Guid tenantId, ValidatedSoftwareEntry entry)
    {
        var vendor = string.IsNullOrWhiteSpace(entry.Vendor) ? "_" : entry.Vendor.Trim().ToLowerInvariant();
        var name = entry.Name.Trim().ToLowerInvariant();
        return $"authscan:{tenantId}:{vendor}:{name}";
    }

    private async Task SaveValidationIssues(Guid scanJobId, List<ValidationIssueRecord> issues, CancellationToken ct)
    {
        if (issues.Count == 0) return;
        var entities = issues.Select(i => ScanJobValidationIssue.Create(scanJobId, i.FieldPath, i.Message, i.EntryIndex));
        await _dbContext.ScanJobValidationIssues.AddRangeAsync(entities, ct);
    }
}
