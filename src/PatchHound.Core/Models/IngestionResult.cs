using PatchHound.Core.Enums;

namespace PatchHound.Core.Models;

public record IngestionResult(
    string ExternalId,
    string Title,
    string Description,
    Severity VendorSeverity,
    decimal? CvssScore,
    string? CvssVector,
    DateTimeOffset? PublishedDate,
    IReadOnlyList<IngestionAffectedAsset> AffectedAssets,
    string? ProductVendor = null,
    string? ProductName = null,
    string? ProductVersion = null,
    IReadOnlyList<IngestionReference>? References = null,
    IReadOnlyList<IngestionAffectedSoftware>? AffectedSoftware = null,
    IReadOnlyList<string>? Sources = null
);

public record IngestionReference(string Url, string Source, IReadOnlyList<string> Tags);

public record IngestionAffectedSoftware(
    bool Vulnerable,
    string Criteria,
    string? VersionStartIncluding,
    string? VersionStartExcluding,
    string? VersionEndIncluding,
    string? VersionEndExcluding
);

public record IngestionAffectedAsset(
    string ExternalAssetId,
    string AssetName,
    AssetType AssetType,
    string? ProductVendor = null,
    string? ProductName = null,
    string? ProductVersion = null
);

public record IngestionAsset(
    string ExternalId,
    string Name,
    AssetType AssetType,
    string? Description = null,
    string? DeviceComputerDnsName = null,
    string? DeviceHealthStatus = null,
    string? DeviceOsPlatform = null,
    string? DeviceOsVersion = null,
    string? DeviceRiskScore = null,
    DateTimeOffset? DeviceLastSeenAt = null,
    string? DeviceLastIpAddress = null,
    string? DeviceAadDeviceId = null,
    string? DeviceGroupId = null,
    string? DeviceGroupName = null,
    string Metadata = "{}"
);

public record IngestionDeviceSoftwareLink(
    string DeviceExternalId,
    string SoftwareExternalId,
    DateTimeOffset ObservedAt
);

public record IngestionAssetInventorySnapshot(
    IReadOnlyList<IngestionAsset> Assets,
    IReadOnlyList<IngestionDeviceSoftwareLink> DeviceSoftwareLinks,
    int RetrievedSoftwareCount = 0,
    int SoftwareWithoutMachineReferencesCount = 0
);
