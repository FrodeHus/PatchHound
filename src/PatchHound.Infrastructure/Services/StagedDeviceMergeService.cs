using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

/// <summary>
/// Merges staged device + device-software-link rows into the canonical
/// <see cref="Device"/>, <see cref="SoftwareProduct"/>, and
/// <see cref="InstalledSoftware"/> tables. Runs under system context
/// (uses <c>IgnoreQueryFilters</c>).
/// </summary>
public class StagedDeviceMergeService(
    PatchHoundDbContext db,
    ISoftwareProductResolver softwareResolver,
    IBulkDeviceMergeWriter bulkDeviceMergeWriter
) : IStagedDeviceMergeService
{
    public async Task<StagedDeviceMergeSummary> MergeAsync(
        Guid ingestionRunId,
        Guid tenantId,
        CancellationToken ct
    )
    {
        var devicesCreated = 0;
        var devicesTouched = 0;
        var installedCreated = 0;
        var installedTouched = 0;
        var devicesSkipped = 0;
        var devicesDeactivated = 0;

        // 1. Load device-type staged rows for this run+tenant.
        var stagedDevices = await db
            .StagedDevices.IgnoreQueryFilters()
            .Where(s =>
                s.IngestionRunId == ingestionRunId
                && s.TenantId == tenantId
                && s.AssetType == AssetType.Device
            )
            .ToListAsync(ct);

        if (stagedDevices.Count == 0)
        {
            return new StagedDeviceMergeSummary(DevicesCreated: 0, DevicesTouched: 0, InstalledSoftwareCreated: 0, InstalledSoftwareTouched: 0);
        }

        // 2. Load software-type staged rows for this run+tenant so we can look
        //    up vendor/name/version by software external id.
        var stagedSoftware = await db
            .StagedDevices.IgnoreQueryFilters()
            .Where(s =>
                s.IngestionRunId == ingestionRunId
                && s.TenantId == tenantId
                && s.AssetType == AssetType.Software
            )
            .ToListAsync(ct);
        var stagedSoftwareByExternalId = stagedSoftware
            .GroupBy(s => s.ExternalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.StagedAt).First(),
                StringComparer.OrdinalIgnoreCase
            );

        // 3. Load all device-software links for this run+tenant and group by
        //    device external id.
        var stagedLinks = await db
            .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(l => l.IngestionRunId == ingestionRunId && l.TenantId == tenantId)
            .ToListAsync(ct);
        var linksByDeviceExternalId = stagedLinks
            .GroupBy(l => l.DeviceExternalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var linkedSoftwareExternalIds = stagedLinks
            .Select(l => l.SoftwareExternalId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 4. Source system key -> entity lookup. Keys are normalized lowercase.
        var sourceSystems = await db
            .SourceSystems.ToDictionaryAsync(s => s.Key, StringComparer.Ordinal, ct);

        var softwareProductIdsByExternalId = await ResolveSoftwareProductIdsByExternalIdAsync(
            stagedSoftwareByExternalId.Values.Where(asset => linkedSoftwareExternalIds.Contains(asset.ExternalId)),
            sourceSystems,
            ct);

        // 5. Pre-load all existing devices for the staged external IDs to avoid N+1 SELECTs.
        //    Key: (SourceSystemId, ExternalId) — handles runs that span multiple source systems.
        //    Use AsNoTracking — the bulk writer is the persistence boundary, not EF change tracking.
        var stagedExternalIds = stagedDevices.Select(d => d.ExternalId).Distinct().ToList();
        var devicesByKey = await db.Devices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && stagedExternalIds.Contains(d.ExternalId))
            .ToDictionaryAsync(d => (d.SourceSystemId, d.ExternalId), d => d, ct);

        // ── Pass 1: Build device merge rows ─────────────────────────────────────────────────
        // For stale+inactive devices we still emit a row (with is_active=false) when the device
        // already exists, so the writer deactivates it. New stale+inactive devices are skipped.
        var deviceRows = new List<DeviceMergeRow>(stagedDevices.Count);
        // Track which staged device rows participated in the upsert so Pass 2 can resolve their
        // canonical ids. Maps (sourceKey, externalId) → (sourceSystemId, externalId).
        var participatingDeviceKeys = new Dictionary<(string SourceKey, string ExternalId), (Guid SourceSystemId, string ExternalId)>();

        foreach (var stagedDevice in stagedDevices)
        {
            var normalizedKey = stagedDevice.SourceKey.Trim().ToLowerInvariant();
            if (!sourceSystems.TryGetValue(normalizedKey, out var sourceSystem))
            {
                throw new InvalidOperationException(
                    $"Unknown source system key '{stagedDevice.SourceKey}'. Seed it before ingesting."
                );
            }

            var payload = JsonSerializer.Deserialize<IngestionAsset>(
                stagedDevice.PayloadJson,
                StagingSerializerOptions.Instance
            );
            if (payload is null)
            {
                throw new InvalidOperationException(
                    $"StagedDevice {stagedDevice.Id} has null payload."
                );
            }

            var existedBefore = devicesByKey.ContainsKey((sourceSystem.Id, stagedDevice.ExternalId));

            if (IsStaleAndInactive(payload))
            {
                if (!existedBefore)
                {
                    devicesSkipped++;
                    continue;
                }

                deviceRows.Add(BuildRow(tenantId, sourceSystem.Id, stagedDevice.ExternalId, stagedDevice.Name, payload, isActive: false));
                participatingDeviceKeys[(stagedDevice.SourceKey, stagedDevice.ExternalId)] = (sourceSystem.Id, stagedDevice.ExternalId);
                devicesDeactivated++;
                continue;
            }

            deviceRows.Add(BuildRow(tenantId, sourceSystem.Id, stagedDevice.ExternalId, stagedDevice.Name, payload, isActive: true));
            participatingDeviceKeys[(stagedDevice.SourceKey, stagedDevice.ExternalId)] = (sourceSystem.Id, stagedDevice.ExternalId);

            if (existedBefore)
            {
                devicesTouched++;
            }
            else
            {
                devicesCreated++;
            }
        }

        // Bulk upsert all devices in a single round-trip.
        var deviceIds = await bulkDeviceMergeWriter.UpsertDevicesAsync(deviceRows, ct);

        // ── Pass 2: Build installed-software merge rows ─────────────────────────────────────
        // Pre-load existing rows so we can distinguish created vs touched in the summary.
        var participatingDeviceIds = deviceIds.Values.Distinct().ToList();
        var existingInstalled = await db.InstalledSoftware
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && participatingDeviceIds.Contains(i.DeviceId))
            .Select(i => new { i.DeviceId, i.SoftwareProductId, i.SourceSystemId, i.Version })
            .ToListAsync(ct);
        var existingInstalledKeys = new HashSet<(Guid, Guid, Guid, string)>(
            existingInstalled.Select(i => (i.DeviceId, i.SoftwareProductId, i.SourceSystemId, i.Version)));

        var installedRows = new List<InstalledSoftwareMergeRow>();
        var seenInstalledKeys = new HashSet<(Guid, Guid, Guid, string)>();

        foreach (var stagedDevice in stagedDevices)
        {
            if (!participatingDeviceKeys.TryGetValue((stagedDevice.SourceKey, stagedDevice.ExternalId), out var deviceLookup))
            {
                continue;
            }

            if (!deviceIds.TryGetValue(deviceLookup, out var canonicalDeviceId))
            {
                continue;
            }

            if (!linksByDeviceExternalId.TryGetValue(stagedDevice.ExternalId, out var deviceLinks))
            {
                continue;
            }

            var normalizedSourceKey = stagedDevice.SourceKey.Trim().ToLowerInvariant();
            var sourceSystem = sourceSystems[normalizedSourceKey];

            foreach (var link in deviceLinks)
            {
                if (!stagedSoftwareByExternalId.TryGetValue(link.SoftwareExternalId, out var stagedSoftwareAsset))
                {
                    continue;
                }

                var (_, _, version) = ExtractSoftwareIdentity(stagedSoftwareAsset);
                var productId = softwareProductIdsByExternalId[stagedSoftwareAsset.ExternalId];

                var normalizedVersion = version?.Trim() ?? string.Empty;
                var key = (canonicalDeviceId, productId, sourceSystem.Id, normalizedVersion);

                if (!seenInstalledKeys.Add(key))
                {
                    // Same logical row already queued for this batch; skip to keep counts honest.
                    continue;
                }

                var observedAt = link.ObservedAt == default ? DateTimeOffset.UtcNow : link.ObservedAt;
                installedRows.Add(new InstalledSoftwareMergeRow(
                    TenantId: tenantId,
                    DeviceId: canonicalDeviceId,
                    SoftwareProductId: productId,
                    SourceSystemId: sourceSystem.Id,
                    Version: normalizedVersion,
                    ObservedAt: observedAt,
                    RunId: ingestionRunId));

                if (existingInstalledKeys.Contains(key))
                {
                    installedTouched++;
                }
                else
                {
                    installedCreated++;
                }
            }
        }

        await bulkDeviceMergeWriter.UpsertInstalledSoftwareAsync(installedRows, ct);

        // Per-source stale-install sweep. Each ingestion run is per-(tenant, source) with
        // full-snapshot semantics: rows the source did not re-report this run are stale.
        // Require two consecutive misses before removal to absorb transient source gaps.
        // Without this sweep, ExposureDerivationService (which is intentionally source-
        // agnostic — see comment in that file) keeps re-deriving exposures from stale
        // installs, which reopens previously-resolved exposures and inflates the
        // "recurred" episode count instead of letting ResolveStaleAsync close them.
        //
        // Scope = sources that actually participated in this run (drawn from the device
        // upsert pass — a source counts as "participating" even if it ingested zero
        // software for some devices, so its stale installs still need pruning).
        // The DeviceVulnerabilityExposure → InstalledSoftware FK is OnDelete(SetNull),
        // so deleting the install simply nulls the back-reference and the exposure
        // stays intact for ResolveStaleAsync to close on its LastSeenRunId mismatch.
        var participatingSourceIds = participatingDeviceKeys.Values
            .Select(v => v.SourceSystemId)
            .Distinct()
            .ToList();
        var installedSoftwareRemoved = 0;
        if (participatingSourceIds.Count > 0)
        {
            var staleQuery = db.InstalledSoftware
                .IgnoreQueryFilters()
                .Where(i => i.TenantId == tenantId
                         && participatingSourceIds.Contains(i.SourceSystemId)
                         && i.LastSeenRunId != ingestionRunId);
            var staleRowsToMarkQuery = staleQuery
                .Where(i => i.LastMissedRunId == null || i.LastMissedRunId != ingestionRunId);

            if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                // InMemory provider does not support ExecuteDeleteAsync; fall back to
                // load + Remove. Production traffic always uses the Postgres path.
                var staleRows = await staleQuery.ToListAsync(ct);
                if (staleRows.Count > 0)
                {
                    foreach (var staleRow in staleRows)
                    {
                        if (staleRow.LastMissedRunId != ingestionRunId)
                        {
                            staleRow.MarkMissing(ingestionRunId);
                        }
                    }

                    var rowsToRemove = staleRows
                        .Where(staleRow => staleRow.MissingSyncCount >= 2)
                        .ToList();
                    db.InstalledSoftware.RemoveRange(rowsToRemove);
                    await db.SaveChangesAsync(ct);
                    installedSoftwareRemoved = rowsToRemove.Count;
                }
            }
            else
            {
                // First sweep after deploy can be large (backlog of never-pruned rows);
                // default 30s command timeout is not enough. This is a batch job — a
                // long ceiling is appropriate. Mirrors ExposureDerivationService.
                var prior = db.Database.GetCommandTimeout();
                db.Database.SetCommandTimeout(600);
                try
                {
                    await staleRowsToMarkQuery.ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                item => item.MissingSyncCount,
                                item => item.MissingSyncCount + 1)
                            .SetProperty(
                                item => item.LastMissedRunId,
                                ingestionRunId),
                        ct);

                    installedSoftwareRemoved = await staleQuery
                        .Where(item => item.MissingSyncCount >= 2)
                        .ExecuteDeleteAsync(ct);
                }
                finally
                {
                    db.Database.SetCommandTimeout(prior);
                }
            }
        }

        return new StagedDeviceMergeSummary(
            DevicesCreated: devicesCreated,
            DevicesTouched: devicesTouched,
            InstalledSoftwareCreated: installedCreated,
            InstalledSoftwareTouched: installedTouched,
            DevicesSkipped: devicesSkipped,
            DevicesDeactivated: devicesDeactivated,
            InstalledSoftwareRemoved: installedSoftwareRemoved
        );
    }

    private async Task<Dictionary<string, Guid>> ResolveSoftwareProductIdsByExternalIdAsync(
        IEnumerable<StagedDevice> linkedSoftwareAssets,
        IReadOnlyDictionary<string, SourceSystem> sourceSystems,
        CancellationToken ct)
    {
        var observations = new List<SoftwareAssetObservation>();
        foreach (var stagedSoftwareAsset in linkedSoftwareAssets)
        {
            var normalizedSourceKey = stagedSoftwareAsset.SourceKey.Trim().ToLowerInvariant();
            if (!sourceSystems.TryGetValue(normalizedSourceKey, out var sourceSystem))
            {
                throw new InvalidOperationException(
                    $"Unknown source system key '{stagedSoftwareAsset.SourceKey}'. Seed it before ingesting."
                );
            }

            var (vendor, productName, _) = ExtractSoftwareIdentity(stagedSoftwareAsset);
            observations.Add(new SoftwareAssetObservation(
                ExternalId: stagedSoftwareAsset.ExternalId,
                SourceSystemId: sourceSystem.Id,
                Vendor: vendor,
                Name: productName,
                CanonicalProductKey: BuildCanonicalProductKey(vendor, productName)));
        }

        if (observations.Count == 0)
        {
            return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        }

        var productIdsByExternalId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var existingAliasKeys = new HashSet<(Guid SourceSystemId, string ExternalId)>();
        var sourceSystemIds = observations.Select(o => o.SourceSystemId).Distinct().ToList();
        var externalIds = observations.Select(o => o.ExternalId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var externalIdChunk in externalIds.Chunk(5_000))
        {
            var chunk = externalIdChunk.ToList();
            var aliases = await db.SoftwareAliases
                .AsNoTracking()
                .Where(a => sourceSystemIds.Contains(a.SourceSystemId) && chunk.Contains(a.ExternalId))
                .ToListAsync(ct);

            foreach (var alias in aliases)
            {
                existingAliasKeys.Add((alias.SourceSystemId, alias.ExternalId));
                productIdsByExternalId[alias.ExternalId] = alias.SoftwareProductId;
            }
        }

        const int aliasSaveBatchSize = 5_000;
        var newAliases = new List<SoftwareAlias>(aliasSaveBatchSize);
        foreach (var group in observations
            .Where(o => !existingAliasKeys.Contains((o.SourceSystemId, o.ExternalId)))
            .GroupBy(o => o.CanonicalProductKey, StringComparer.Ordinal))
        {
            var representative = group.First();
            var product = await softwareResolver.ResolveAsync(
                new SoftwareObservation(
                    SourceSystemId: representative.SourceSystemId,
                    ExternalId: representative.ExternalId,
                    Vendor: representative.Vendor,
                    Name: representative.Name
                ),
                ct);

            var productId = product.Id;
            existingAliasKeys.Add((representative.SourceSystemId, representative.ExternalId));

            foreach (var observation in group)
            {
                productIdsByExternalId[observation.ExternalId] = productId;

                if (observation == representative)
                {
                    continue;
                }

                var aliasKey = (observation.SourceSystemId, observation.ExternalId);
                if (!existingAliasKeys.Add(aliasKey))
                {
                    continue;
                }

                newAliases.Add(SoftwareAlias.Create(
                    softwareProductId: productId,
                    sourceSystemId: observation.SourceSystemId,
                    externalId: observation.ExternalId,
                    observedVendor: observation.Vendor,
                    observedName: observation.Name));

                if (newAliases.Count >= aliasSaveBatchSize)
                {
                    await FlushNewAliasesAsync(newAliases, ct);
                }
            }
        }

        if (newAliases.Count > 0)
        {
            await FlushNewAliasesAsync(newAliases, ct);
        }

        return productIdsByExternalId;
    }

    private async Task FlushNewAliasesAsync(List<SoftwareAlias> aliases, CancellationToken ct)
    {
        db.SoftwareAliases.AddRange(aliases);
        await db.SaveChangesAsync(ct);

        foreach (var alias in aliases)
        {
            db.Entry(alias).State = EntityState.Detached;
        }

        aliases.Clear();
    }

    private static DeviceMergeRow BuildRow(
        Guid tenantId,
        Guid sourceSystemId,
        string externalId,
        string name,
        IngestionAsset payload,
        bool isActive)
    {
        return new DeviceMergeRow(
            TenantId: tenantId,
            SourceSystemId: sourceSystemId,
            ExternalId: externalId,
            Name: name,
            ComputerDnsName: payload.DeviceComputerDnsName,
            HealthStatus: payload.DeviceHealthStatus,
            OsPlatform: payload.DeviceOsPlatform,
            OsVersion: payload.DeviceOsVersion,
            ExternalRiskLabel: payload.DeviceRiskScore,
            LastSeenAt: payload.DeviceLastSeenAt,
            LastIpAddress: payload.DeviceLastIpAddress,
            AadDeviceId: payload.DeviceAadDeviceId,
            GroupId: payload.DeviceGroupId,
            GroupName: payload.DeviceGroupName,
            ExposureLevel: payload.DeviceExposureLevel,
            IsAadJoined: payload.DeviceIsAadJoined,
            OnboardingStatus: payload.DeviceOnboardingStatus,
            DeviceValue: payload.DeviceValue,
            IsActive: isActive);
    }

    /// <summary>
    /// Returns true if the device payload indicates it is stale (last seen over
    /// 30 days ago) AND the source system has already marked it Inactive.
    /// Stale+inactive new devices are skipped; existing ones are deactivated.
    /// </summary>
    private static bool IsStaleAndInactive(IngestionAsset payload)
    {
        if (!string.Equals(payload.DeviceHealthStatus, "Inactive", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        return payload.DeviceLastSeenAt.HasValue && payload.DeviceLastSeenAt.Value < cutoff;
    }

    private static string BuildCanonicalProductKey(string vendor, string name)
        => $"{vendor.Trim().ToLowerInvariant()}::{name.Trim().ToLowerInvariant()}";

    /// <summary>
    /// Extracts vendor / product name / version from a software-type
    /// <see cref="StagedDevice"/>. Defender ingestion stores these as fields
    /// in the <see cref="IngestionAsset.Metadata"/> JSON blob (see
    /// <c>DefenderVulnerabilitySource.NormalizeSoftwareAsset</c>).
    /// Falls back to the raw asset name when metadata is unavailable.
    /// </summary>
    private static (string Vendor, string Name, string? Version) ExtractSoftwareIdentity(
        StagedDevice stagedSoftware
    )
    {
        string vendor = "unknown";
        string name = stagedSoftware.Name;
        string? version = null;

        IngestionAsset? payload = null;
        try
        {
            payload = JsonSerializer.Deserialize<IngestionAsset>(
                stagedSoftware.PayloadJson,
                StagingSerializerOptions.Instance
            );
        }
        catch (JsonException)
        {
            // Fall through to defaults.
        }

        if (payload is null)
        {
            return (vendor, name, version);
        }

        if (!string.IsNullOrWhiteSpace(payload.Metadata))
        {
            try
            {
                using var document = JsonDocument.Parse(payload.Metadata);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (
                        root.TryGetProperty("vendor", out var vendorProp)
                        && vendorProp.ValueKind == JsonValueKind.String
                    )
                    {
                        var vendorValue = vendorProp.GetString();
                        if (!string.IsNullOrWhiteSpace(vendorValue))
                        {
                            vendor = vendorValue.Trim();
                        }
                    }

                    if (
                        root.TryGetProperty("name", out var nameProp)
                        && nameProp.ValueKind == JsonValueKind.String
                    )
                    {
                        var nameValue = nameProp.GetString();
                        if (!string.IsNullOrWhiteSpace(nameValue))
                        {
                            name = nameValue.Trim();
                        }
                    }

                    if (
                        root.TryGetProperty("version", out var versionProp)
                        && versionProp.ValueKind == JsonValueKind.String
                    )
                    {
                        var versionValue = versionProp.GetString();
                        if (!string.IsNullOrWhiteSpace(versionValue))
                        {
                            version = versionValue.Trim();
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Fall through to defaults.
            }
        }

        return (vendor, name, version);
    }

    private sealed record SoftwareAssetObservation(
        string ExternalId,
        Guid SourceSystemId,
        string Vendor,
        string Name,
        string CanonicalProductKey);
}
