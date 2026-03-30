# Creating a New Ingestion Source

This guide walks through adding a new vulnerability ingestion source to PatchHound. It covers the simplest approach: implement one interface, register it, and let the framework handle scheduling, staging, and merging.

## Overview

```
Your Source Class
  implements IVulnerabilitySource
  returns List<IngestionResult>
       |
       v
IngestionService (stages + merges results)
       ^
       |
IngestionWorker (polls on cron schedule)
```

## Step 1: Create the Source Class

Create a new file in `src/PatchHound.Infrastructure/VulnerabilitySources/`.

Implement `IVulnerabilitySource` — it has two properties and one method:

```csharp
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.VulnerabilitySources;

public class ExampleVulnerabilitySource : IVulnerabilitySource
{
    public string SourceKey => "example-source";   // unique identifier, used in DB
    public string SourceName => "ExampleSource";   // display name, stored in Sources[]

    private readonly HttpClient _httpClient;
    private readonly ILogger<ExampleVulnerabilitySource> _logger;

    public ExampleVulnerabilitySource(
        HttpClient httpClient,
        ILogger<ExampleVulnerabilitySource> logger
    )
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<IngestionResult>> FetchVulnerabilitiesAsync(
        Guid tenantId,
        CancellationToken ct
    )
    {
        // 1. Fetch raw data from your external API
        var response = await _httpClient.GetFromJsonAsync<List<RawItem>>("/vulns", ct);
        if (response is null) return [];

        // 2. Transform each item into an IngestionResult
        return response.Select(item => new IngestionResult(
            ExternalId: item.CveId,                          // e.g. "CVE-2024-1234"
            Title: item.Title,
            Description: item.Description,
            VendorSeverity: ParseSeverity(item.Severity),    // Severity enum
            CvssScore: item.CvssScore,
            CvssVector: item.CvssVector,
            PublishedDate: item.PublishedDate,
            AffectedAssets: item.Machines.Select(m => new IngestionAffectedAsset(
                ExternalAssetId: m.Id,
                AssetName: m.Name,
                AssetType: AssetType.Device
            )).ToList(),
            Sources: [SourceName]
        )).ToList();
    }

    private static Severity ParseSeverity(string value) => value.ToLowerInvariant() switch
    {
        "critical" => Severity.Critical,
        "high"     => Severity.High,
        "medium"   => Severity.Medium,
        "low"      => Severity.Low,
        _          => Severity.Medium,
    };
}
```

### IngestionResult Fields

| Field | Required | Description |
|-------|----------|-------------|
| `ExternalId` | Yes | CVE ID or unique vulnerability identifier |
| `Title` | Yes | Short display name |
| `Description` | Yes | Full description text |
| `VendorSeverity` | Yes | `Severity.Low/Medium/High/Critical` |
| `CvssScore` | No | CVSS base score (decimal) |
| `CvssVector` | No | CVSS vector string |
| `PublishedDate` | No | When the vulnerability was published |
| `AffectedAssets` | Yes | List of machines/devices affected |
| `ProductVendor` | No | Software vendor name |
| `ProductName` | No | Software product name |
| `ProductVersion` | No | Affected version |
| `References` | No | External links (`IngestionReference`) |
| `AffectedSoftware` | No | CPE-style software match rules |
| `Sources` | No | Origin tags, e.g. `["MySource"]` |

## Step 2: Register in DI

Edit `src/PatchHound.Infrastructure/DependencyInjection.cs` and add your source:

```csharp
// Vulnerability Sources
services.AddScoped<IVulnerabilitySource, DefenderVulnerabilitySource>();
services.AddScoped<IVulnerabilitySource, ExampleVulnerabilitySource>();  // <-- add this

// If your source needs a typed HttpClient:
services.AddHttpClient<ExampleVulnerabilitySource>();
```

The `IngestionService` constructor receives `IEnumerable<IVulnerabilitySource>` — your source is automatically discovered.

## Step 3: Register in the Source Catalog

Edit `src/PatchHound.Infrastructure/Tenants/TenantSourceCatalog.cs`:

```csharp
public const string ExampleSourceKey = "example-source";  // must match SourceKey
public const string DefaultExampleSchedule = "0 */12 * * *";  // every 12 hours
```

Add a factory method and include it in `CreateDefaults` so new tenants get the source configuration, or create `TenantSourceConfiguration` rows via a migration for existing tenants.

## Step 4: Configure Credentials (if needed)

If your source requires per-tenant API credentials, use the existing `TenantSourceConfiguration` entity which provides:

- `CredentialTenantId` / `ClientId` — identity fields
- `SecretRef` — reference to a secret stored in OpenBao (Vault)
- `ApiBaseUrl` / `TokenScope` — endpoint configuration

Load the configuration in your source via the `tenantId` parameter passed to `FetchVulnerabilitiesAsync`.

## What the Framework Handles

Once your source is registered, PatchHound automatically:

- **Schedules** runs based on the cron expression in `TenantSourceConfiguration.SyncSchedule`
- **Prevents concurrent runs** via lease management
- **Stages** your `IngestionResult` records into temporary tables
- **Merges** staged data into the main vulnerability/asset tables (dedup, update, create)
- **Tracks run status** (LastStartedAt, LastCompletedAt, LastStatus, LastError)
- **Supports manual triggers** from the admin UI

## Optional: Batch Source for Large Datasets

If your API returns thousands of records, implement `IVulnerabilityBatchSource` alongside `IVulnerabilitySource` for cursor-based pagination:

```csharp
public class ExampleVulnerabilitySource : IVulnerabilitySource, IVulnerabilityBatchSource
{
    // ... IVulnerabilitySource implementation from above ...

    public async Task<SourceBatchResult<IngestionResult>> FetchVulnerabilityBatchAsync(
        Guid tenantId,
        string? cursorJson,
        int batchSize,
        CancellationToken ct
    )
    {
        var cursor = cursorJson is not null
            ? JsonSerializer.Deserialize<MyCursor>(cursorJson)
            : new MyCursor(0);

        var items = await FetchPage(cursor.Offset, batchSize, ct);
        var isComplete = items.Count < batchSize;

        return new SourceBatchResult<IngestionResult>(
            Items: items,
            NextCursorJson: isComplete ? null : JsonSerializer.Serialize(new MyCursor(cursor.Offset + batchSize)),
            IsComplete: isComplete
        );
    }

    private record MyCursor(int Offset);
}
```

When both interfaces are implemented, the framework prefers the batch path automatically.

## Optional: Asset Inventory Source

To also ingest machines and installed software, implement `IAssetInventorySource`:

```csharp
public async Task<IngestionAssetInventorySnapshot> FetchAssetsAsync(
    Guid tenantId, CancellationToken ct)
{
    var devices = await FetchDevices(ct);
    var software = await FetchSoftware(ct);

    return new IngestionAssetInventorySnapshot(
        Assets: devices.Concat(software).ToList(),
        DeviceSoftwareLinks: BuildLinks(devices, software)
    );
}
```
