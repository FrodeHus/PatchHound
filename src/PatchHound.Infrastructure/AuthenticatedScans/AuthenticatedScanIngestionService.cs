using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Infrastructure.AuthenticatedScans;

public class AuthenticatedScanIngestionService(
    PatchHoundDbContext dbContext,
    AuthenticatedScanOutputValidator validator,
    IStagedDeviceMergeService stagedDeviceMergeService,
    NormalizedSoftwareProjectionService projectionService)
{
    private const string SourceKey = "authenticated-scan";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ProcessJobResultAsync(Guid scanJobId, string rawStdout, string rawStderr, CancellationToken ct)
    {
        var job = await dbContext.ScanJobs.FirstOrDefaultAsync(j => j.Id == scanJobId, ct)
            ?? throw new InvalidOperationException($"ScanJob {scanJobId} not found");

        var result = ScanJobResult.Create(scanJobId, rawStdout, rawStderr, string.Empty);
        await dbContext.ScanJobResults.AddAsync(result, ct);

        var validation = validator.Validate(rawStdout);

        if (validation.FatalError)
        {
            job.CompleteFailed(ScanJobStatuses.Failed, $"Validation: {validation.FatalErrorMessage}", DateTimeOffset.UtcNow);
            await SaveValidationIssues(scanJobId, validation.Issues, ct);
            await dbContext.SaveChangesAsync(ct);
            return;
        }

        await SaveValidationIssues(scanJobId, validation.Issues, ct);

        if (validation.ValidEntries.Count == 0)
        {
            job.CompleteSucceeded(rawStdout.Length, rawStderr.Length, 0, DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(ct);
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

        await dbContext.StagedDevices.AddRangeAsync(stagedSoftware, ct);
        await dbContext.StagedDeviceSoftwareInstallations.AddRangeAsync(softwareLinks, ct);
        await dbContext.SaveChangesAsync(ct);
        dbContext.ChangeTracker.Clear();

        // Merge + resolve + project through the canonical pipeline
        await stagedDeviceMergeService.MergeAsync(ingestionRunId, job.TenantId, ct);
        await projectionService.SyncTenantAsync(job.TenantId, null, ct);

        job = await dbContext.ScanJobs.FirstAsync(j => j.Id == scanJobId, ct);
        job.CompleteSucceeded(rawStdout.Length, rawStderr.Length, validation.ValidEntries.Count, DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(ct);
    }

    private async Task<string> GetDeviceExternalId(Guid deviceId, CancellationToken ct)
    {
        var device = await dbContext.Devices.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == deviceId, ct);
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
        await dbContext.ScanJobValidationIssues.AddRangeAsync(entities, ct);
    }
}

