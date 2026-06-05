using PatchHound.Core.Enums;

namespace PatchHound.Core.Interfaces;

public sealed record IngestionObservationBatch(
    Guid TenantId,
    Guid IngestionRunId,
    Guid SourceSystemId,
    IReadOnlyList<DeviceObservationInput> Devices,
    IReadOnlyList<SoftwareObservationInput> Software,
    IReadOnlyList<InstallationObservationInput> Installations,
    IReadOnlyList<VulnerabilityObservationInput> Vulnerabilities,
    IReadOnlyList<ExposureObservationInput> Exposures)
{
    public static IngestionObservationBatch Create(
        Guid tenantId,
        Guid ingestionRunId,
        Guid sourceSystemId,
        IReadOnlyList<DeviceObservationInput>? devices = null,
        IReadOnlyList<SoftwareObservationInput>? software = null,
        IReadOnlyList<InstallationObservationInput>? installations = null,
        IReadOnlyList<VulnerabilityObservationInput>? vulnerabilities = null,
        IReadOnlyList<ExposureObservationInput>? exposures = null)
    {
        RequireGuid(tenantId, nameof(tenantId));
        RequireGuid(ingestionRunId, nameof(ingestionRunId));
        RequireGuid(sourceSystemId, nameof(sourceSystemId));

        return new IngestionObservationBatch(
            tenantId,
            ingestionRunId,
            sourceSystemId,
            devices ?? [],
            software ?? [],
            installations ?? [],
            vulnerabilities ?? [],
            exposures ?? []);
    }

    internal static void RequireGuid(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{name} is required.", name);
        }
    }

    internal static string Required(string? value, string name, int maxLength)
    {
        var normalized = Optional(value, name, maxLength);
        if (normalized is null)
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return normalized;
    }

    internal static string? Optional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"{name} must be {maxLength} characters or fewer.", name);
        }

        return normalized;
    }
}

public sealed record DeviceObservationInput(
    string ExternalId,
    string Name,
    DateTimeOffset ObservedAt,
    int BatchNumber,
    string? ComputerDnsName,
    string? HealthStatus,
    string? OsPlatform,
    string? OsVersion,
    DateTimeOffset? SourceLastSeenAt)
{
    public static DeviceObservationInput Create(
        string externalId,
        string name,
        DateTimeOffset observedAt,
        int batchNumber = 0,
        string? computerDnsName = null,
        string? healthStatus = null,
        string? osPlatform = null,
        string? osVersion = null,
        DateTimeOffset? sourceLastSeenAt = null)
    {
        RequireTimestamp(observedAt, nameof(observedAt));
        return new DeviceObservationInput(
            IngestionObservationBatch.Required(externalId, nameof(externalId), 256),
            IngestionObservationBatch.Required(name, nameof(name), 512),
            observedAt,
            batchNumber,
            IngestionObservationBatch.Optional(computerDnsName, nameof(computerDnsName), 256),
            IngestionObservationBatch.Optional(healthStatus, nameof(healthStatus), 64),
            IngestionObservationBatch.Optional(osPlatform, nameof(osPlatform), 128),
            IngestionObservationBatch.Optional(osVersion, nameof(osVersion), 128),
            sourceLastSeenAt);
    }

    internal static void RequireTimestamp(DateTimeOffset value, string name)
    {
        if (value == default)
        {
            throw new ArgumentException($"{name} is required.", name);
        }
    }
}

public sealed record SoftwareObservationInput(
    string ExternalId,
    string Vendor,
    string Name,
    string? Version,
    string CanonicalProductKey,
    DateTimeOffset ObservedAt,
    int BatchNumber)
{
    public static SoftwareObservationInput Create(
        string externalId,
        string vendor,
        string name,
        string? version,
        DateTimeOffset observedAt,
        int batchNumber = 0)
    {
        DeviceObservationInput.RequireTimestamp(observedAt, nameof(observedAt));
        var normalizedVendor = IngestionObservationBatch.Required(vendor, nameof(vendor), 256);
        var normalizedName = IngestionObservationBatch.Required(name, nameof(name), 512);
        var canonicalKey = $"{normalizedVendor.ToLowerInvariant()}::{normalizedName.ToLowerInvariant()}";
        if (canonicalKey.Length > 512)
        {
            throw new ArgumentException("CanonicalProductKey must be 512 characters or fewer.", nameof(name));
        }

        return new SoftwareObservationInput(
            IngestionObservationBatch.Required(externalId, nameof(externalId), 256),
            normalizedVendor,
            normalizedName,
            IngestionObservationBatch.Optional(version, nameof(version), 128),
            canonicalKey,
            observedAt,
            batchNumber);
    }
}

public sealed record InstallationObservationInput(
    string DeviceExternalId,
    string SoftwareExternalId,
    string Version,
    DateTimeOffset ObservedAt,
    int BatchNumber)
{
    public static InstallationObservationInput Create(
        string deviceExternalId,
        string softwareExternalId,
        string? version,
        DateTimeOffset observedAt,
        int batchNumber = 0)
    {
        DeviceObservationInput.RequireTimestamp(observedAt, nameof(observedAt));
        return new InstallationObservationInput(
            IngestionObservationBatch.Required(deviceExternalId, nameof(deviceExternalId), 256),
            IngestionObservationBatch.Required(softwareExternalId, nameof(softwareExternalId), 256),
            IngestionObservationBatch.Optional(version, nameof(version), 128) ?? string.Empty,
            observedAt,
            batchNumber);
    }
}

public sealed record VulnerabilityObservationInput(
    string ExternalId,
    string Title,
    string Description,
    Severity VendorSeverity,
    decimal? CvssScore,
    string? CvssVector,
    DateTimeOffset? PublishedDate,
    DateTimeOffset ObservedAt,
    int BatchNumber)
{
    public static VulnerabilityObservationInput Create(
        string externalId,
        string title,
        Severity vendorSeverity,
        DateTimeOffset observedAt,
        string? description = null,
        decimal? cvssScore = null,
        string? cvssVector = null,
        DateTimeOffset? publishedDate = null,
        int batchNumber = 0)
    {
        DeviceObservationInput.RequireTimestamp(observedAt, nameof(observedAt));
        return new VulnerabilityObservationInput(
            IngestionObservationBatch.Required(externalId, nameof(externalId), 128),
            IngestionObservationBatch.Required(title, nameof(title), 512),
            description?.Trim() ?? string.Empty,
            vendorSeverity,
            cvssScore,
            IngestionObservationBatch.Optional(cvssVector, nameof(cvssVector), 256),
            publishedDate,
            observedAt,
            batchNumber);
    }
}

public sealed record ExposureObservationInput(
    string DeviceExternalId,
    string VulnerabilityExternalId,
    string SoftwareExternalId,
    string? SoftwareVersion,
    DateTimeOffset ObservedAt,
    int BatchNumber)
{
    public static ExposureObservationInput Create(
        string deviceExternalId,
        string vulnerabilityExternalId,
        string? softwareExternalId,
        string? softwareVersion,
        DateTimeOffset observedAt,
        int batchNumber = 0)
    {
        DeviceObservationInput.RequireTimestamp(observedAt, nameof(observedAt));
        return new ExposureObservationInput(
            IngestionObservationBatch.Required(deviceExternalId, nameof(deviceExternalId), 256),
            IngestionObservationBatch.Required(vulnerabilityExternalId, nameof(vulnerabilityExternalId), 128),
            IngestionObservationBatch.Optional(softwareExternalId, nameof(softwareExternalId), 256) ?? string.Empty,
            IngestionObservationBatch.Optional(softwareVersion, nameof(softwareVersion), 128),
            observedAt,
            batchNumber);
    }
}

public sealed record ObservationLoadSummary(
    int DeviceCount,
    int SoftwareCount,
    int InstallationCount,
    int VulnerabilityCount,
    int ExposureCount);

public interface IObservationBulkLoader
{
    Task<ObservationLoadSummary> LoadAsync(IngestionObservationBatch batch, CancellationToken ct);
}
