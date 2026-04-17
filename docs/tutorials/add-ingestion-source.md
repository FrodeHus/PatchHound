# Adding an Ingestion Source

This tutorial walks through adding a new tenant-scoped ingestion source. PatchHound supports three source capabilities that can be combined on a single class:

| Interface | What it provides |
|---|---|
| `IVulnerabilitySource` | CVE-level vulnerability records (full or batch) |
| `IAssetInventorySource` | Device and software inventory (full or batch) |
| `ICloudApplicationSource` | Entra app registrations and credential metadata |

A source can implement any combination of these. The worker discovers sources through DI and routes them automatically.

---

## Step 1 — Register the source key

Add constants to `TenantSourceCatalog` in `src/PatchHound.Infrastructure/Tenants/TenantSourceCatalog.cs`:

```csharp
public const string AcmeSourceKey = "acme-scanner";
public const string DefaultAcmeSchedule = "0 */6 * * *";
public const string DefaultAcmeApiBaseUrl = "https://api.acme.example/v1";
```

Add a factory method and include it in `CreateDefaults`:

```csharp
public static TenantSourceConfiguration CreateDefaultAcme(Guid tenantId) =>
    TenantSourceConfiguration.Create(
        tenantId,
        AcmeSourceKey,
        "Acme Scanner",
        enabled: false,
        DefaultAcmeSchedule,
        apiBaseUrl: DefaultAcmeApiBaseUrl
    );

public static IReadOnlyList<TenantSourceConfiguration> CreateDefaults(Guid tenantId)
{
    return [CreateDefaultDefender(tenantId), CreateDefaultEntraApplications(tenantId), CreateDefaultAcme(tenantId)];
}
```

Also add the key to `SupportsScheduling` / `SupportsManualSync`:

```csharp
public static bool SupportsScheduling(TenantSourceConfiguration source) =>
    source.SourceKey is DefenderSourceKey or EntraApplicationsSourceKey or AcmeSourceKey;
```

---

## Step 2 — Create a configuration provider

Add `src/PatchHound.Infrastructure/CredentialSources/AcmeConfigurationProvider.cs`:

```csharp
public class AcmeConfigurationProvider(PatchHoundDbContext dbContext, ISecretStore secretStore)
{
    public virtual async Task<AcmeClientConfiguration?> GetConfigurationAsync(
        Guid tenantId, CancellationToken ct)
    {
        var source = await dbContext.TenantSourceConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.TenantId == tenantId && s.SourceKey == TenantSourceCatalog.AcmeSourceKey, ct);

        if (source is null || !source.Enabled)
            return null;

        if (!TenantSourceCatalog.HasConfiguredCredentials(source))
        {
            var hasPartial = !string.IsNullOrWhiteSpace(source.CredentialTenantId)
                || !string.IsNullOrWhiteSpace(source.ClientId)
                || !string.IsNullOrWhiteSpace(source.SecretRef);
            if (hasPartial)
                throw new IngestionTerminalException("Acme Scanner source is enabled but credentials are incomplete.");
            return null;
        }

        var apiKey = string.Empty;
        if (!string.IsNullOrWhiteSpace(source.SecretRef))
            apiKey = await secretStore.GetSecretAsync(source.SecretRef, "clientSecret", ct) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(source.ClientId) || string.IsNullOrWhiteSpace(apiKey))
            throw new IngestionTerminalException("Acme Scanner source credentials could not be resolved.");

        return new AcmeClientConfiguration(source.ClientId, apiKey, source.ApiBaseUrl);
    }
}

public record AcmeClientConfiguration(string ClientId, string ApiKey, string ApiBaseUrl);
```

---

## Step 3 — Create an API client

Add `src/PatchHound.Infrastructure/CredentialSources/AcmeApiClient.cs`. Inject `HttpClient` via DI — the HTTP client is registered with retry/timeout policies automatically.

```csharp
public class AcmeApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<AcmeFinding>> GetFindingsAsync(
        AcmeClientConfiguration config, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{config.ApiBaseUrl}/findings");
        request.Headers.Add("X-Api-Key", config.ApiKey);

        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<AcmeFindingsResponse>(cancellationToken: ct);
        return payload?.Findings ?? [];
    }
}
```

---

## Step 4 — Implement the source

Add `src/PatchHound.Infrastructure/VulnerabilitySources/AcmeVulnerabilitySource.cs`.

Implement `IVulnerabilitySource` at minimum. Add `IVulnerabilityBatchSource` if the upstream API supports cursor-based pagination (preferred for large datasets). Add `IAssetInventorySource` or `ICloudApplicationSource` if the source also provides those.

```csharp
public class AcmeVulnerabilitySource(
    AcmeApiClient apiClient,
    AcmeConfigurationProvider configurationProvider,
    ILogger<AcmeVulnerabilitySource> logger
) : IVulnerabilitySource
{
    public string SourceKey => TenantSourceCatalog.AcmeSourceKey;
    public string SourceName => "AcmeScanner";

    public async Task<IReadOnlyList<IngestionResult>> FetchVulnerabilitiesAsync(
        Guid tenantId, CancellationToken ct)
    {
        var config = await configurationProvider.GetConfigurationAsync(tenantId, ct);
        if (config is null)
        {
            logger.LogInformation("Skipping Acme ingestion for tenant {TenantId}: no enabled credentials.", tenantId);
            return [];
        }

        var findings = await apiClient.GetFindingsAsync(config, ct);
        return findings.Select(MapToIngestionResult).ToList();
    }

    public Task<CanonicalVulnerabilityBatch> FetchCanonicalVulnerabilitiesAsync(
        Guid tenantId, CancellationToken ct) =>
        Task.FromResult(new CanonicalVulnerabilityBatch([], []));

    private static IngestionResult MapToIngestionResult(AcmeFinding finding) =>
        new(
            ExternalId: finding.CveId,
            DeviceExternalId: finding.AssetId,
            Source: "AcmeScanner",
            // ... map remaining fields
        );
}
```

If the API is large and supports pagination, implement `IVulnerabilityBatchSource` instead of (or in addition to) `IVulnerabilitySource`. The ingestion worker prefers batch sources when available.

```csharp
public async Task<SourceBatchResult<IngestionResult>> FetchVulnerabilityBatchAsync(
    Guid tenantId, string? cursorJson, int batchSize, CancellationToken ct)
{
    var cursor = cursorJson is not null
        ? JsonSerializer.Deserialize<AcmeCursor>(cursorJson)
        : null;

    var page = await apiClient.GetFindingsPageAsync(config, cursor?.Page ?? 1, batchSize, ct);

    var nextCursor = page.HasMore
        ? JsonSerializer.Serialize(new AcmeCursor(cursor?.Page + 1 ?? 2))
        : null;

    return new SourceBatchResult<IngestionResult>(page.Items.Select(MapToIngestionResult).ToList(), nextCursor);
}
```

---

## Step 5 — Register in DI

In `src/PatchHound.Infrastructure/DependencyInjection.cs`:

```csharp
// Vulnerability Sources
services.AddScoped<IVulnerabilitySource, DefenderVulnerabilitySource>();
services.AddScoped<IVulnerabilitySource, EntraApplicationSource>();
services.AddScoped<IVulnerabilitySource, AcmeVulnerabilitySource>(); // ← add

// Configuration providers
services.AddScoped<AcmeConfigurationProvider>();

// HTTP clients — AddExternalHttpPolicies wires retry + timeout
services.AddHttpClient<AcmeApiClient>().AddExternalHttpPolicies(maxConnectionsPerServer: 2);
```

---

## Step 6 — Apply the default source row

Existing tenants get the new source row backfilled on their next GET in the admin UI (via `BuildTenantDetailDto`). New tenants get it from `TenantSourceCatalog.CreateDefaults`. No migration is required for the source configuration row itself.

If you need a DB schema change (e.g., a new credential field on `TenantSourceConfiguration`), generate a migration:

```bash
dotnet ef migrations add AddAcmeSourceCredentialField \
  --project src/PatchHound.Infrastructure \
  --startup-project src/PatchHound.Api
```

---

## Step 7 — Expose the source in the admin UI

The admin UI renders credential and schedule fields for any source key returned by the API. No frontend changes are needed for basic credential/schedule editing.

If the source supports credential linking (like Entra Applications reusing Defender credentials), add the checkbox UI in `TenantSourceManagement.tsx` following the `entra-applications` pattern (search for `source.key === 'entra-applications'`).

Update the sidebar if the source produces a new browsable asset type — add an entry to the `Assets` group in `frontend/src/components/layout/Sidebar.tsx`.

---

## Source capability quick reference

```
IVulnerabilitySource              — full snapshot per run
IVulnerabilityBatchSource         — cursor-paginated, preferred for large APIs
IAssetInventorySource             — full device/software snapshot
IAssetInventoryBatchSource        — cursor-paginated device/software
ICloudApplicationSource           — Entra app registrations + credential metadata
```

A single class can implement any combination. The ingestion service checks for each interface at runtime and runs whichever capabilities are present.
