using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Tests.TestData;

/// <summary>
/// In-memory replacement for <see cref="IBulkDeviceMergeWriter"/> used by tests
/// that run against the EF Core InMemory provider (which cannot execute the
/// real PostgreSQL temp-table + ON CONFLICT writer).
///
/// SEMANTIC DIVERGENCE vs production <c>PostgresBulkDeviceMergeWriter</c>:
/// - Uses EF change-tracking + <c>Device.Create</c> / <c>UpdateInventoryDetails</c>
///   to mimic upsert semantics. Updates touch the same mutable columns as the
///   production writer; criticality/owner columns are untouched on update.
/// - Calls <c>SaveChangesAsync</c> at the end of each operation, matching the
///   transactional commit boundary of the Postgres writer.
/// </summary>
internal sealed class InMemoryBulkDeviceMergeWriter(PatchHoundDbContext db) : IBulkDeviceMergeWriter
{
    public async Task<IReadOnlyDictionary<(Guid SourceSystemId, string ExternalId), Guid>>
        UpsertDevicesAsync(IReadOnlyCollection<DeviceMergeRow> rows, CancellationToken ct)
    {
        var map = new Dictionary<(Guid SourceSystemId, string ExternalId), Guid>(rows.Count);
        if (rows.Count == 0) return map;

        // Pre-load existing devices for the (tenant, source, externalId) triples in rows.
        var externalIds = rows.Select(r => r.ExternalId).Distinct().ToList();
        var tenantIds = rows.Select(r => r.TenantId).Distinct().ToList();
        var existing = await db.Devices
            .IgnoreQueryFilters()
            .Where(d => tenantIds.Contains(d.TenantId) && externalIds.Contains(d.ExternalId))
            .ToListAsync(ct);
        var existingByKey = existing.ToDictionary(d => (d.TenantId, d.SourceSystemId, d.ExternalId), d => d);

        foreach (var r in rows)
        {
            if (existingByKey.TryGetValue((r.TenantId, r.SourceSystemId, r.ExternalId), out var device))
            {
                device.Rename(r.Name);
                device.UpdateInventoryDetails(
                    computerDnsName: r.ComputerDnsName,
                    healthStatus: r.HealthStatus,
                    osPlatform: r.OsPlatform,
                    osVersion: r.OsVersion,
                    externalRiskLabel: r.ExternalRiskLabel,
                    lastSeenAt: r.LastSeenAt,
                    lastIpAddress: r.LastIpAddress,
                    aadDeviceId: r.AadDeviceId,
                    groupId: r.GroupId,
                    groupName: r.GroupName,
                    exposureLevel: r.ExposureLevel,
                    isAadJoined: r.IsAadJoined,
                    onboardingStatus: r.OnboardingStatus,
                    deviceValue: r.DeviceValue);
                device.SetActiveInTenant(r.IsActive);
                map[(r.SourceSystemId, r.ExternalId)] = device.Id;
            }
            else
            {
                var fresh = Device.Create(
                    tenantId: r.TenantId,
                    sourceSystemId: r.SourceSystemId,
                    externalId: r.ExternalId,
                    name: r.Name,
                    baselineCriticality: Criticality.Medium);
                fresh.UpdateInventoryDetails(
                    computerDnsName: r.ComputerDnsName,
                    healthStatus: r.HealthStatus,
                    osPlatform: r.OsPlatform,
                    osVersion: r.OsVersion,
                    externalRiskLabel: r.ExternalRiskLabel,
                    lastSeenAt: r.LastSeenAt,
                    lastIpAddress: r.LastIpAddress,
                    aadDeviceId: r.AadDeviceId,
                    groupId: r.GroupId,
                    groupName: r.GroupName,
                    exposureLevel: r.ExposureLevel,
                    isAadJoined: r.IsAadJoined,
                    onboardingStatus: r.OnboardingStatus,
                    deviceValue: r.DeviceValue);
                fresh.SetActiveInTenant(r.IsActive);
                db.Devices.Add(fresh);
                existingByKey[(r.TenantId, r.SourceSystemId, r.ExternalId)] = fresh;
                map[(r.SourceSystemId, r.ExternalId)] = fresh.Id;
            }
        }

        await db.SaveChangesAsync(ct);
        return map;
    }

    public async Task<int> UpsertInstalledSoftwareAsync(
        IReadOnlyCollection<InstalledSoftwareMergeRow> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0) return 0;

        var deviceIds = rows.Select(r => r.DeviceId).Distinct().ToList();
        var existing = await db.InstalledSoftware
            .IgnoreQueryFilters()
            .Where(i => deviceIds.Contains(i.DeviceId))
            .ToListAsync(ct);
        var byKey = existing.ToDictionary(
            i => (i.TenantId, i.DeviceId, i.SoftwareProductId, i.SourceSystemId, i.Version),
            i => i);

        var touched = 0;
        foreach (var r in rows)
        {
            var version = r.Version ?? string.Empty;
            var key = (r.TenantId, r.DeviceId, r.SoftwareProductId, r.SourceSystemId, version);
            if (byKey.TryGetValue(key, out var hit))
            {
                hit.Touch(r.ObservedAt, r.RunId);
            }
            else
            {
                var fresh = InstalledSoftware.Observe(
                    tenantId: r.TenantId,
                    deviceId: r.DeviceId,
                    softwareProductId: r.SoftwareProductId,
                    sourceSystemId: r.SourceSystemId,
                    version: version,
                    at: r.ObservedAt == default ? DateTimeOffset.UtcNow : r.ObservedAt,
                    runId: r.RunId);
                db.InstalledSoftware.Add(fresh);
                byKey[key] = fresh;
            }
            touched++;
        }

        await db.SaveChangesAsync(ct);
        return touched;
    }
}
