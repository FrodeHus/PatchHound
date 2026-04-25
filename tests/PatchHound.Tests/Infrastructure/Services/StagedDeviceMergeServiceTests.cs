using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;
using PatchHound.Infrastructure.Services.Inventory;

namespace PatchHound.Tests.Infrastructure.Services;

public class StagedDeviceMergeServiceTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private SourceSystem _sourceSystem = null!;
    private StagedDeviceMergeService _sut = null!;

    public async ValueTask InitializeAsync()
    {
        _db = await TestDbContextFactory.CreateAsync();
        _sourceSystem = SourceSystem.Create("defender", "Defender");
        _db.SourceSystems.Add(_sourceSystem);
        await _db.SaveChangesAsync();

        _sut = new StagedDeviceMergeService(
            _db,
            new DeviceResolver(_db),
            new SoftwareProductResolver(_db)
        );
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Merge_creates_device_and_installed_software_from_staged_rows()
    {
        var runId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await SeedStagedDeviceWithSoftwareAsync(
            runId: runId,
            tenantId: tenantId,
            deviceExternalId: "dev-001",
            deviceName: "workstation-01",
            softwareExternalId: "defender-sw::mozilla_firefox::120.0",
            softwareAssetName: "Firefox 120.0",
            vendor: "Mozilla",
            productName: "Firefox",
            version: "120.0"
        );

        var summary = await _sut.MergeAsync(runId, tenantId, CancellationToken.None);

        summary.DevicesCreated.Should().Be(1);
        summary.DevicesTouched.Should().Be(0);
        summary.InstalledSoftwareCreated.Should().Be(1);
        summary.InstalledSoftwareTouched.Should().Be(0);

        var devices = await _db.Devices.IgnoreQueryFilters().ToListAsync();
        devices.Should().ContainSingle();
        devices[0].TenantId.Should().Be(tenantId);
        devices[0].SourceSystemId.Should().Be(_sourceSystem.Id);
        devices[0].ExternalId.Should().Be("dev-001");
        devices[0].OsPlatform.Should().Be("Windows11");
        devices[0].ComputerDnsName.Should().Be("workstation-01.contoso.local");

        var installed = await _db.InstalledSoftware.IgnoreQueryFilters().ToListAsync();
        installed.Should().ContainSingle();
        installed[0].TenantId.Should().Be(tenantId);
        installed[0].DeviceId.Should().Be(devices[0].Id);
        installed[0].Version.Should().Be("120.0");

        var products = await _db.SoftwareProducts.ToListAsync();
        products.Should().ContainSingle();
        products[0].Vendor.Should().Be("Mozilla");
        products[0].Name.Should().Be("Firefox");
    }

    [Fact]
    public async Task Merge_is_idempotent_on_repeated_run()
    {
        var tenantId = Guid.NewGuid();
        var run1Id = Guid.NewGuid();
        var run2Id = Guid.NewGuid();

        await SeedStagedDeviceWithSoftwareAsync(
            runId: run1Id,
            tenantId: tenantId,
            deviceExternalId: "dev-idem",
            deviceName: "host-idem",
            softwareExternalId: "defender-sw::acme_widget::1.0",
            softwareAssetName: "Widget 1.0",
            vendor: "Acme",
            productName: "Widget",
            version: "1.0"
        );
        await SeedStagedDeviceWithSoftwareAsync(
            runId: run2Id,
            tenantId: tenantId,
            deviceExternalId: "dev-idem",
            deviceName: "host-idem",
            softwareExternalId: "defender-sw::acme_widget::1.0",
            softwareAssetName: "Widget 1.0",
            vendor: "Acme",
            productName: "Widget",
            version: "1.0"
        );

        var firstSummary = await _sut.MergeAsync(run1Id, tenantId, CancellationToken.None);
        var secondSummary = await _sut.MergeAsync(run2Id, tenantId, CancellationToken.None);

        firstSummary.DevicesCreated.Should().Be(1);
        firstSummary.InstalledSoftwareCreated.Should().Be(1);

        secondSummary.DevicesCreated.Should().Be(0);
        secondSummary.DevicesTouched.Should().Be(1);
        secondSummary.InstalledSoftwareCreated.Should().Be(0);
        secondSummary.InstalledSoftwareTouched.Should().BeGreaterThanOrEqualTo(1);

        var devices = await _db.Devices.IgnoreQueryFilters().ToListAsync();
        devices.Should().ContainSingle();

        var installed = await _db.InstalledSoftware.IgnoreQueryFilters().ToListAsync();
        installed.Should().ContainSingle();

        var products = await _db.SoftwareProducts.ToListAsync();
        products.Should().ContainSingle();
    }

    [Fact]
    public async Task Merge_two_tenants_does_not_cross_contaminate()
    {
        var runId = Guid.NewGuid();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await SeedStagedDeviceWithSoftwareAsync(
            runId: runId,
            tenantId: tenantA,
            deviceExternalId: "shared-ext",
            deviceName: "host-tenant-a",
            softwareExternalId: "defender-sw::contoso_office::2024",
            softwareAssetName: "Office 2024",
            vendor: "Contoso",
            productName: "Office",
            version: "2024"
        );
        await SeedStagedDeviceWithSoftwareAsync(
            runId: runId,
            tenantId: tenantB,
            deviceExternalId: "shared-ext",
            deviceName: "host-tenant-b",
            softwareExternalId: "defender-sw::contoso_office::2024",
            softwareAssetName: "Office 2024",
            vendor: "Contoso",
            productName: "Office",
            version: "2024"
        );

        await _sut.MergeAsync(runId, tenantA, CancellationToken.None);
        await _sut.MergeAsync(runId, tenantB, CancellationToken.None);

        var devices = await _db.Devices.IgnoreQueryFilters().ToListAsync();
        devices.Should().HaveCount(2);
        devices.Select(d => d.TenantId).Should().BeEquivalentTo(new[] { tenantA, tenantB });

        var products = await _db.SoftwareProducts.ToListAsync();
        products.Should().ContainSingle("SoftwareProduct is a global canonical entity shared across tenants");

        var installed = await _db.InstalledSoftware.IgnoreQueryFilters().ToListAsync();
        installed.Should().HaveCount(2);
        installed.Select(i => i.SoftwareProductId).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Merge_skips_new_stale_inactive_device()
    {
        var runId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await SeedStagedDeviceAsync(
            runId: runId,
            tenantId: tenantId,
            deviceExternalId: "stale-new",
            deviceName: "ghost-device",
            healthStatus: "Inactive",
            lastSeenAt: DateTimeOffset.UtcNow.AddDays(-45)
        );

        var summary = await _sut.MergeAsync(runId, tenantId, CancellationToken.None);

        summary.DevicesSkipped.Should().Be(1);
        summary.DevicesCreated.Should().Be(0);
        summary.DevicesDeactivated.Should().Be(0);

        var devices = await _db.Devices.IgnoreQueryFilters().ToListAsync();
        devices.Should().BeEmpty("stale inactive new devices must not be created");
    }

    [Fact]
    public async Task Merge_deactivates_existing_stale_inactive_device()
    {
        var tenantId = Guid.NewGuid();
        var run1Id = Guid.NewGuid();
        var run2Id = Guid.NewGuid();

        // First run: device is active and healthy.
        await SeedStagedDeviceAsync(
            runId: run1Id,
            tenantId: tenantId,
            deviceExternalId: "dev-goes-stale",
            deviceName: "retiring-host",
            healthStatus: "Active",
            lastSeenAt: DateTimeOffset.UtcNow.AddMinutes(-10)
        );
        await _sut.MergeAsync(run1Id, tenantId, CancellationToken.None);

        var deviceAfterFirstRun = await _db.Devices.IgnoreQueryFilters()
            .SingleAsync(d => d.ExternalId == "dev-goes-stale");
        deviceAfterFirstRun.ActiveInTenant.Should().BeTrue();

        // Second run: same device is now stale+inactive.
        await SeedStagedDeviceAsync(
            runId: run2Id,
            tenantId: tenantId,
            deviceExternalId: "dev-goes-stale",
            deviceName: "retiring-host",
            healthStatus: "Inactive",
            lastSeenAt: DateTimeOffset.UtcNow.AddDays(-60)
        );
        var summary = await _sut.MergeAsync(run2Id, tenantId, CancellationToken.None);

        summary.DevicesDeactivated.Should().Be(1);
        summary.DevicesCreated.Should().Be(0);
        summary.DevicesTouched.Should().Be(0);

        var deviceAfterSecondRun = await _db.Devices.IgnoreQueryFilters()
            .SingleAsync(d => d.ExternalId == "dev-goes-stale");
        deviceAfterSecondRun.ActiveInTenant.Should().BeFalse();
    }

    [Fact]
    public async Task Merge_reactivates_device_when_seen_again_after_being_inactive()
    {
        var tenantId = Guid.NewGuid();
        var run1Id = Guid.NewGuid();
        var run2Id = Guid.NewGuid();
        var run3Id = Guid.NewGuid();

        // Run 1: active.
        await SeedStagedDeviceAsync(
            runId: run1Id,
            tenantId: tenantId,
            deviceExternalId: "dev-revives",
            deviceName: "returning-host",
            healthStatus: "Active",
            lastSeenAt: DateTimeOffset.UtcNow.AddMinutes(-5)
        );
        await _sut.MergeAsync(run1Id, tenantId, CancellationToken.None);

        // Run 2: stale+inactive → deactivated.
        await SeedStagedDeviceAsync(
            runId: run2Id,
            tenantId: tenantId,
            deviceExternalId: "dev-revives",
            deviceName: "returning-host",
            healthStatus: "Inactive",
            lastSeenAt: DateTimeOffset.UtcNow.AddDays(-45)
        );
        await _sut.MergeAsync(run2Id, tenantId, CancellationToken.None);

        var afterDeactivation = await _db.Devices.IgnoreQueryFilters()
            .SingleAsync(d => d.ExternalId == "dev-revives");
        afterDeactivation.ActiveInTenant.Should().BeFalse();

        // Run 3: device shows up as active again → should be reactivated.
        await SeedStagedDeviceAsync(
            runId: run3Id,
            tenantId: tenantId,
            deviceExternalId: "dev-revives",
            deviceName: "returning-host",
            healthStatus: "Active",
            lastSeenAt: DateTimeOffset.UtcNow.AddMinutes(-2)
        );
        var summary = await _sut.MergeAsync(run3Id, tenantId, CancellationToken.None);

        summary.DevicesTouched.Should().Be(1);
        summary.DevicesDeactivated.Should().Be(0);

        var afterReactivation = await _db.Devices.IgnoreQueryFilters()
            .SingleAsync(d => d.ExternalId == "dev-revives");
        afterReactivation.ActiveInTenant.Should().BeTrue();
    }

    private async Task SeedStagedDeviceAsync(
        Guid runId,
        Guid tenantId,
        string deviceExternalId,
        string deviceName,
        string healthStatus,
        DateTimeOffset lastSeenAt
    )
    {
        var deviceAsset = new IngestionAsset(
            ExternalId: deviceExternalId,
            Name: deviceName,
            AssetType: AssetType.Device,
            Description: null,
            DeviceComputerDnsName: $"{deviceName}.contoso.local",
            DeviceHealthStatus: healthStatus,
            DeviceOsPlatform: "Windows11",
            DeviceOsVersion: "10.0.22631",
            DeviceRiskScore: "Medium",
            DeviceLastSeenAt: lastSeenAt,
            DeviceLastIpAddress: "10.0.0.5",
            DeviceAadDeviceId: Guid.NewGuid().ToString()
        );
        var stagedDevice = StagedDevice.Create(
            ingestionRunId: runId,
            tenantId: tenantId,
            sourceKey: "defender",
            externalId: deviceExternalId,
            name: deviceName,
            assetType: AssetType.Device,
            payloadJson: JsonSerializer.Serialize(deviceAsset),
            stagedAt: DateTimeOffset.UtcNow
        );
        _db.StagedDevices.Add(stagedDevice);
        await _db.SaveChangesAsync();
    }

    private async Task SeedStagedDeviceWithSoftwareAsync(
        Guid runId,
        Guid tenantId,
        string deviceExternalId,
        string deviceName,
        string softwareExternalId,
        string softwareAssetName,
        string vendor,
        string productName,
        string version
    )
    {
        var deviceAsset = new IngestionAsset(
            ExternalId: deviceExternalId,
            Name: deviceName,
            AssetType: AssetType.Device,
            Description: null,
            DeviceComputerDnsName: $"{deviceName}.contoso.local",
            DeviceHealthStatus: "Active",
            DeviceOsPlatform: "Windows11",
            DeviceOsVersion: "10.0.22631",
            DeviceRiskScore: "Medium",
            DeviceLastSeenAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            DeviceLastIpAddress: "10.0.0.5",
            DeviceAadDeviceId: Guid.NewGuid().ToString()
        );
        var devicePayloadJson = JsonSerializer.Serialize(deviceAsset);
        var stagedDevice = StagedDevice.Create(
            ingestionRunId: runId,
            tenantId: tenantId,
            sourceKey: "defender",
            externalId: deviceExternalId,
            name: deviceName,
            assetType: AssetType.Device,
            payloadJson: devicePayloadJson,
            stagedAt: DateTimeOffset.UtcNow
        );
        _db.StagedDevices.Add(stagedDevice);

        var softwareMetadata = JsonSerializer.Serialize(
            new
            {
                softwareId = softwareExternalId,
                name = productName,
                vendor = vendor,
                version = version,
                derivedFromSoftwareInventory = true,
            }
        );
        var softwareAsset = new IngestionAsset(
            ExternalId: softwareExternalId,
            Name: softwareAssetName,
            AssetType: AssetType.Software,
            Description: softwareAssetName,
            Metadata: softwareMetadata
        );
        var softwarePayloadJson = JsonSerializer.Serialize(softwareAsset);
        var stagedSoftware = StagedDevice.Create(
            ingestionRunId: runId,
            tenantId: tenantId,
            sourceKey: "defender",
            externalId: softwareExternalId,
            name: softwareAssetName,
            assetType: AssetType.Software,
            payloadJson: softwarePayloadJson,
            stagedAt: DateTimeOffset.UtcNow
        );
        _db.StagedDevices.Add(stagedSoftware);

        var link = new IngestionDeviceSoftwareLink(
            DeviceExternalId: deviceExternalId,
            SoftwareExternalId: softwareExternalId,
            ObservedAt: DateTimeOffset.UtcNow
        );
        var linkPayloadJson = JsonSerializer.Serialize(link);
        var stagedLink = StagedDeviceSoftwareInstallation.Create(
            ingestionRunId: runId,
            tenantId: tenantId,
            sourceKey: "defender",
            deviceExternalId: deviceExternalId,
            softwareExternalId: softwareExternalId,
            observedAt: link.ObservedAt,
            payloadJson: linkPayloadJson,
            stagedAt: DateTimeOffset.UtcNow
        );
        _db.StagedDeviceSoftwareInstallations.Add(stagedLink);

        await _db.SaveChangesAsync();
    }
}
