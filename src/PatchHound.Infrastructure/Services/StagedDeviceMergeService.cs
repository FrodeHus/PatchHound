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
/// (uses <c>IgnoreQueryFilters</c>) and never touches legacy
/// <c>Assets</c>/<c>DeviceSoftwareInstallations</c> tables.
/// </summary>
public class StagedDeviceMergeService(
    PatchHoundDbContext db,
    IDeviceResolver deviceResolver,
    ISoftwareProductResolver softwareResolver
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
            return new StagedDeviceMergeSummary(0, 0, 0, 0);
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
        var stagedSoftwareByExternalId = stagedSoftware.ToDictionary(
            s => s.ExternalId,
            StringComparer.Ordinal
        );

        // 3. Load all device-software links for this run+tenant and group by
        //    device external id.
        var stagedLinks = await db
            .StagedDeviceSoftwareInstallations.IgnoreQueryFilters()
            .Where(l => l.IngestionRunId == ingestionRunId && l.TenantId == tenantId)
            .ToListAsync(ct);
        var linksByDeviceExternalId = stagedLinks
            .GroupBy(l => l.DeviceExternalId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // 4. Source system key -> entity lookup. Keys are normalized lowercase.
        var sourceSystems = await db
            .SourceSystems.ToDictionaryAsync(s => s.Key, StringComparer.Ordinal, ct);

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

            // Pre-check to track created vs touched counts.
            var deviceBefore = await db
                .Devices.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    d =>
                        d.TenantId == tenantId
                        && d.SourceSystemId == sourceSystem.Id
                        && d.ExternalId == stagedDevice.ExternalId,
                    ct
                );

            var device = await deviceResolver.ResolveAsync(
                new DeviceObservation(
                    TenantId: tenantId,
                    SourceSystemId: sourceSystem.Id,
                    ExternalId: stagedDevice.ExternalId,
                    Name: stagedDevice.Name,
                    BaselineCriticality: Criticality.Medium
                ),
                ct
            );

            if (deviceBefore is null)
            {
                devicesCreated++;
            }
            else
            {
                devicesTouched++;
            }

            device.UpdateInventoryDetails(
                computerDnsName: payload.DeviceComputerDnsName,
                healthStatus: payload.DeviceHealthStatus,
                osPlatform: payload.DeviceOsPlatform,
                osVersion: payload.DeviceOsVersion,
                externalRiskLabel: payload.DeviceRiskScore,
                lastSeenAt: payload.DeviceLastSeenAt,
                lastIpAddress: payload.DeviceLastIpAddress,
                aadDeviceId: payload.DeviceAadDeviceId,
                groupId: payload.DeviceGroupId,
                groupName: payload.DeviceGroupName,
                exposureLevel: payload.DeviceExposureLevel,
                isAadJoined: payload.DeviceIsAadJoined,
                onboardingStatus: payload.DeviceOnboardingStatus,
                deviceValue: payload.DeviceValue
            );
            device.SetActiveInTenant(true);

            if (
                !linksByDeviceExternalId.TryGetValue(
                    stagedDevice.ExternalId,
                    out var deviceLinks
                )
            )
            {
                continue;
            }

            foreach (var link in deviceLinks)
            {
                if (
                    !stagedSoftwareByExternalId.TryGetValue(
                        link.SoftwareExternalId,
                        out var stagedSoftwareAsset
                    )
                )
                {
                    // Link references a software external id that was not staged
                    // as a software asset in this run. Skip - no metadata to
                    // resolve a canonical product from.
                    continue;
                }

                var (vendor, productName, version) = ExtractSoftwareIdentity(
                    stagedSoftwareAsset
                );

                var product = await softwareResolver.ResolveAsync(
                    new SoftwareObservation(
                        SourceSystemId: sourceSystem.Id,
                        ExternalId: stagedSoftwareAsset.ExternalId,
                        Vendor: vendor,
                        Name: productName
                    ),
                    ct
                );

                var normalizedVersion = version?.Trim() ?? string.Empty;
                var existing = await db
                    .InstalledSoftware.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        i =>
                            i.TenantId == tenantId
                            && i.DeviceId == device.Id
                            && i.SoftwareProductId == product.Id
                            && i.SourceSystemId == sourceSystem.Id
                            && i.Version == normalizedVersion,
                        ct
                    );

                if (existing is null)
                {
                    var observedAt =
                        link.ObservedAt == default ? DateTimeOffset.UtcNow : link.ObservedAt;
                    db.InstalledSoftware.Add(
                        InstalledSoftware.Observe(
                            tenantId: tenantId,
                            deviceId: device.Id,
                            softwareProductId: product.Id,
                            sourceSystemId: sourceSystem.Id,
                            version: normalizedVersion,
                            at: observedAt
                        )
                    );
                    installedCreated++;
                }
                else
                {
                    existing.Touch(
                        link.ObservedAt == default ? DateTimeOffset.UtcNow : link.ObservedAt
                    );
                    installedTouched++;
                }
            }
        }

        await db.SaveChangesAsync(ct);

        return new StagedDeviceMergeSummary(
            DevicesCreated: devicesCreated,
            DevicesTouched: devicesTouched,
            InstalledSoftwareCreated: installedCreated,
            InstalledSoftwareTouched: installedTouched
        );
    }

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
}
