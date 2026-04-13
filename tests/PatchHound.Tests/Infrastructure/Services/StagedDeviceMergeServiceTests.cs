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

    public async Task InitializeAsync()
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

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
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
        var stagedDevice = StagedAsset.Create(
            ingestionRunId: runId,
            tenantId: tenantId,
            sourceKey: "defender",
            externalId: deviceExternalId,
            name: deviceName,
            assetType: AssetType.Device,
            payloadJson: devicePayloadJson,
            stagedAt: DateTimeOffset.UtcNow
        );
        _db.StagedAssets.Add(stagedDevice);

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
        var stagedSoftware = StagedAsset.Create(
            ingestionRunId: runId,
            tenantId: tenantId,
            sourceKey: "defender",
            externalId: softwareExternalId,
            name: softwareAssetName,
            assetType: AssetType.Software,
            payloadJson: softwarePayloadJson,
            stagedAt: DateTimeOffset.UtcNow
        );
        _db.StagedAssets.Add(stagedSoftware);

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
