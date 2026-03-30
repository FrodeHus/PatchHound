# Microsoft Sentinel CCF Push Connector — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Forward audit log events to Microsoft Sentinel in real-time via the Logs Ingestion API, with a global admin UI for configuration.

**Architecture:** Channel-based in-memory queue sits between the EF audit interceptor and a hosted BackgroundService that micro-batches events and POSTs them to the Sentinel DCE endpoint. Configuration stored in a single DB row with the client secret in OpenBao vault.

**Tech Stack:** .NET 10, EF Core, System.Threading.Channels, Microsoft.Identity.Client (MSAL), React/TanStack Start, Zod, shadcn/ui

---

### Task 1: SentinelAuditEvent record and SentinelConnectorConfiguration entity

**Files:**
- Create: `src/PatchHound.Core/Models/SentinelAuditEvent.cs`
- Create: `src/PatchHound.Core/Entities/SentinelConnectorConfiguration.cs`

- [ ] **Step 1: Create the SentinelAuditEvent record**

```csharp
// src/PatchHound.Core/Models/SentinelAuditEvent.cs
namespace PatchHound.Core.Models;

public sealed record SentinelAuditEvent(
    Guid AuditEntryId,
    Guid TenantId,
    string EntityType,
    Guid EntityId,
    string Action,
    string? OldValues,
    string? NewValues,
    Guid UserId,
    DateTimeOffset Timestamp
);
```

- [ ] **Step 2: Create the SentinelConnectorConfiguration entity**

```csharp
// src/PatchHound.Core/Entities/SentinelConnectorConfiguration.cs
namespace PatchHound.Core.Entities;

public class SentinelConnectorConfiguration
{
    public Guid Id { get; private set; }
    public bool Enabled { get; private set; }
    public string DceEndpoint { get; private set; } = string.Empty;
    public string DcrImmutableId { get; private set; } = string.Empty;
    public string StreamName { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string ClientId { get; private set; } = string.Empty;
    public string SecretRef { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }

    private SentinelConnectorConfiguration() { }

    public static SentinelConnectorConfiguration Create(
        bool enabled,
        string dceEndpoint,
        string dcrImmutableId,
        string streamName,
        string tenantId,
        string clientId,
        string secretRef
    )
    {
        return new SentinelConnectorConfiguration
        {
            Id = Guid.NewGuid(),
            Enabled = enabled,
            DceEndpoint = dceEndpoint,
            DcrImmutableId = dcrImmutableId,
            StreamName = streamName,
            TenantId = tenantId,
            ClientId = clientId,
            SecretRef = secretRef,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        bool enabled,
        string dceEndpoint,
        string dcrImmutableId,
        string streamName,
        string tenantId,
        string clientId,
        string secretRef
    )
    {
        Enabled = enabled;
        DceEndpoint = dceEndpoint;
        DcrImmutableId = dcrImmutableId;
        StreamName = streamName;
        TenantId = tenantId;
        ClientId = clientId;
        SecretRef = secretRef;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Core/Models/SentinelAuditEvent.cs src/PatchHound.Core/Entities/SentinelConnectorConfiguration.cs
git commit -m "feat(sentinel): add SentinelAuditEvent record and SentinelConnectorConfiguration entity"
```

---

### Task 2: EF Core configuration, DbContext registration, and migration

**Files:**
- Create: `src/PatchHound.Infrastructure/Data/Configurations/SentinelConnectorConfigurationConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs:104` — add DbSet
- Generated: new migration file via `dotnet ef`

- [ ] **Step 1: Create the EF configuration**

```csharp
// src/PatchHound.Infrastructure/Data/Configurations/SentinelConnectorConfigurationConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class SentinelConnectorConfigurationConfiguration
    : IEntityTypeConfiguration<SentinelConnectorConfiguration>
{
    public void Configure(EntityTypeBuilder<SentinelConnectorConfiguration> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.DceEndpoint).HasMaxLength(512);
        builder.Property(c => c.DcrImmutableId).HasMaxLength(256);
        builder.Property(c => c.StreamName).HasMaxLength(256);
        builder.Property(c => c.TenantId).HasMaxLength(128);
        builder.Property(c => c.ClientId).HasMaxLength(128);
        builder.Property(c => c.SecretRef).HasMaxLength(256);
    }
}
```

- [ ] **Step 2: Add DbSet to PatchHoundDbContext**

Add after line 104 (`public DbSet<RemediationWorkflowStageRecord> RemediationWorkflowStageRecords => ...;`):

```csharp
    public DbSet<SentinelConnectorConfiguration> SentinelConnectorConfigurations =>
        Set<SentinelConnectorConfiguration>();
```

No query filter needed — this is a global config table, not tenant-scoped.

- [ ] **Step 3: Generate migration**

```bash
cd /Users/frode.hus/src/github.com/frodehus/PatchHound
dotnet ef migrations add AddSentinelConnectorConfiguration \
  --project src/PatchHound.Infrastructure \
  --startup-project src/PatchHound.Api
```

Expected: new migration file created in `src/PatchHound.Infrastructure/Data/Migrations/`.

- [ ] **Step 4: Verify build**

```bash
dotnet build PatchHound.slnx -v minimal
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Data/Configurations/SentinelConnectorConfigurationConfiguration.cs \
  src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs \
  src/PatchHound.Infrastructure/Data/Migrations/
git commit -m "feat(sentinel): add EF configuration, DbSet, and migration for SentinelConnectorConfiguration"
```

---

### Task 3: SentinelAuditQueue singleton

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/SentinelAuditQueue.cs`
- Create: `tests/PatchHound.Tests/Infrastructure/Services/SentinelAuditQueueTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/PatchHound.Tests/Infrastructure/Services/SentinelAuditQueueTests.cs
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Tests.Infrastructure.Services;

public class SentinelAuditQueueTests
{
    private static SentinelAuditEvent CreateEvent(string entityType = "TestEntity") =>
        new(
            AuditEntryId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            EntityType: entityType,
            EntityId: Guid.NewGuid(),
            Action: "Created",
            OldValues: null,
            NewValues: """{"Name":"test"}""",
            UserId: Guid.NewGuid(),
            Timestamp: DateTimeOffset.UtcNow
        );

    [Fact]
    public void TryWrite_returns_true_when_channel_has_capacity()
    {
        var queue = new SentinelAuditQueue();
        var result = queue.TryWrite(CreateEvent());
        Assert.True(result);
    }

    [Fact]
    public async Task Written_event_can_be_read_back()
    {
        var queue = new SentinelAuditQueue();
        var ev = CreateEvent("Vulnerability");

        queue.TryWrite(ev);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await foreach (var item in queue.ReadAllAsync(cts.Token))
        {
            Assert.Equal("Vulnerability", item.EntityType);
            Assert.Equal(ev.AuditEntryId, item.AuditEntryId);
            return;
        }

        Assert.Fail("No event read from queue");
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

```bash
dotnet test PatchHound.slnx --filter "FullyQualifiedName~SentinelAuditQueueTests" -v minimal
```

Expected: compilation errors — `SentinelAuditQueue` does not exist.

- [ ] **Step 3: Implement SentinelAuditQueue**

```csharp
// src/PatchHound.Infrastructure/Services/SentinelAuditQueue.cs
using System.Threading.Channels;
using PatchHound.Core.Models;

namespace PatchHound.Infrastructure.Services;

public sealed class SentinelAuditQueue
{
    private readonly Channel<SentinelAuditEvent> _channel = Channel.CreateBounded<SentinelAuditEvent>(
        new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        }
    );

    public bool TryWrite(SentinelAuditEvent auditEvent) => _channel.Writer.TryWrite(auditEvent);

    public IAsyncEnumerable<SentinelAuditEvent> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
```

- [ ] **Step 4: Run tests — verify they pass**

```bash
dotnet test PatchHound.slnx --filter "FullyQualifiedName~SentinelAuditQueueTests" -v minimal
```

Expected: 2 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/SentinelAuditQueue.cs \
  tests/PatchHound.Tests/Infrastructure/Services/SentinelAuditQueueTests.cs
git commit -m "feat(sentinel): add SentinelAuditQueue with bounded channel"
```

---

### Task 4: Wire SentinelAuditQueue into AuditSaveChangesInterceptor

**Files:**
- Modify: `src/PatchHound.Infrastructure/Data/AuditSaveChangesInterceptor.cs:31-36` — add queue field + constructor param
- Modify: `src/PatchHound.Infrastructure/Data/AuditSaveChangesInterceptor.cs:99-109` — enqueue after creating audit entry
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs:36` — register queue singleton before interceptor

- [ ] **Step 1: Add SentinelAuditQueue to the interceptor constructor**

In `AuditSaveChangesInterceptor.cs`, replace lines 31-36:

```csharp
    private readonly ITenantContext _tenantContext;
    private readonly SentinelAuditQueue? _sentinelQueue;

    public AuditSaveChangesInterceptor(
        ITenantContext tenantContext,
        SentinelAuditQueue? sentinelQueue = null
    )
    {
        _tenantContext = tenantContext;
        _sentinelQueue = sentinelQueue;
    }
```

- [ ] **Step 2: Enqueue events after creating audit entries**

In `AuditSaveChangesInterceptor.cs`, after line 109 (`context.Set<AuditLogEntry>().Add(auditEntry);`), add:

```csharp
            _sentinelQueue?.TryWrite(new PatchHound.Core.Models.SentinelAuditEvent(
                AuditEntryId: auditEntry.Id,
                TenantId: auditEntry.TenantId,
                EntityType: auditEntry.EntityType,
                EntityId: auditEntry.EntityId,
                Action: action.ToString(),
                OldValues: oldValues,
                NewValues: newValues,
                UserId: auditEntry.UserId,
                Timestamp: auditEntry.Timestamp
            ));
```

- [ ] **Step 3: Register SentinelAuditQueue singleton in DI**

In `DependencyInjection.cs`, add before line 36 (`services.AddScoped<AuditSaveChangesInterceptor>();`):

```csharp
        services.AddSingleton<SentinelAuditQueue>();
```

- [ ] **Step 4: Verify build**

```bash
dotnet build PatchHound.slnx -v minimal
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Run existing tests**

```bash
dotnet test PatchHound.slnx -v minimal
```

Expected: all existing tests still pass.

- [ ] **Step 6: Commit**

```bash
git add src/PatchHound.Infrastructure/Data/AuditSaveChangesInterceptor.cs \
  src/PatchHound.Infrastructure/DependencyInjection.cs
git commit -m "feat(sentinel): wire SentinelAuditQueue into AuditSaveChangesInterceptor"
```

---

### Task 5: SentinelConnectorWorker BackgroundService

**Files:**
- Create: `src/PatchHound.Infrastructure/Services/SentinelConnectorWorker.cs`
- Modify: `src/PatchHound.Infrastructure/DependencyInjection.cs` — register hosted service

- [ ] **Step 1: Add MSAL NuGet package**

```bash
dotnet add src/PatchHound.Infrastructure/PatchHound.Infrastructure.csproj package Microsoft.Identity.Client
```

- [ ] **Step 2: Create the worker**

```csharp
// src/PatchHound.Infrastructure/Services/SentinelConnectorWorker.cs
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.Services;

public sealed class SentinelConnectorWorker : BackgroundService
{
    private const int MaxBatchSize = 100;
    private static readonly TimeSpan BatchWindow = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ConfigPollInterval = TimeSpan.FromSeconds(30);
    private static readonly string[] MonitorScope = ["https://monitor.azure.com/.default"];

    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SentinelAuditQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SentinelConnectorWorker> _logger;

    private IConfidentialClientApplication? _msalApp;
    private string _cachedClientId = string.Empty;
    private string _cachedTenantId = string.Empty;

    public SentinelConnectorWorker(
        SentinelAuditQueue queue,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<SentinelConnectorWorker> logger
    )
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SentinelConnectorWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var config = await LoadConfigAsync(stoppingToken);

            if (config is null || !config.Enabled)
            {
                await DrainAsync(stoppingToken);
                await DelayOrDrain(ConfigPollInterval, stoppingToken);
                continue;
            }

            var batch = await CollectBatchAsync(stoppingToken);
            if (batch.Count == 0)
                continue;

            await PushBatchAsync(config, batch, stoppingToken);
        }
    }

    private async Task<List<SentinelAuditEvent>> CollectBatchAsync(CancellationToken ct)
    {
        var batch = new List<SentinelAuditEvent>(MaxBatchSize);
        using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        batchCts.CancelAfter(BatchWindow);

        try
        {
            await foreach (var item in _queue.ReadAllAsync(batchCts.Token))
            {
                batch.Add(item);
                if (batch.Count >= MaxBatchSize)
                    break;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Batch window expired — return what we have
        }

        return batch;
    }

    private async Task PushBatchAsync(
        SentinelConnectorConfiguration config,
        List<SentinelAuditEvent> batch,
        CancellationToken ct
    )
    {
        try
        {
            var token = await AcquireTokenAsync(config, ct);
            var url =
                $"{config.DceEndpoint.TrimEnd('/')}/dataCollectionRules/{config.DcrImmutableId}"
                + $"/streams/{config.StreamName}?api-version=2023-01-01";

            var payload = batch.Select(e => new
            {
                TimeGenerated = e.Timestamp.UtcDateTime.ToString("O"),
                TenantId = e.TenantId.ToString(),
                e.EntityType,
                EntityId = e.EntityId.ToString(),
                e.Action,
                e.OldValues,
                e.NewValues,
                UserId = e.UserId.ToString(),
                AuditEntryId = e.AuditEntryId.ToString(),
            });

            using var client = _httpClientFactory.CreateClient("SentinelConnector");
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(payload, options: PayloadJsonOptions),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Sentinel push failed with {StatusCode}: {Body}. Dropped {Count} events",
                    response.StatusCode,
                    body.Length > 500 ? body[..500] : body,
                    batch.Count
                );
            }
            else
            {
                _logger.LogDebug("Pushed {Count} audit events to Sentinel", batch.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to push {Count} audit events to Sentinel", batch.Count);
        }
    }

    private async Task<string> AcquireTokenAsync(
        SentinelConnectorConfiguration config,
        CancellationToken ct
    )
    {
        if (_msalApp is null
            || _cachedClientId != config.ClientId
            || _cachedTenantId != config.TenantId)
        {
            var secret = await LoadClientSecretAsync(config, ct);
            _msalApp = ConfidentialClientApplicationBuilder
                .Create(config.ClientId)
                .WithClientSecret(secret)
                .WithAuthority($"https://login.microsoftonline.com/{config.TenantId}")
                .Build();
            _cachedClientId = config.ClientId;
            _cachedTenantId = config.TenantId;
        }

        var result = await _msalApp.AcquireTokenForClient(MonitorScope).ExecuteAsync(ct);
        return result.AccessToken;
    }

    private async Task<string> LoadClientSecretAsync(
        SentinelConnectorConfiguration config,
        CancellationToken ct
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var secretStore = scope.ServiceProvider.GetRequiredService<ISecretStore>();
        var secrets = await secretStore.GetSecretAsync(config.SecretRef, ct);
        return secrets.TryGetValue("clientSecret", out var value)
            ? value
            : throw new InvalidOperationException(
                $"Client secret not found at vault path '{config.SecretRef}'"
            );
    }

    private async Task<SentinelConnectorConfiguration?> LoadConfigAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PatchHoundDbContext>();
            return await dbContext.SentinelConnectorConfigurations
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load Sentinel connector configuration");
            return null;
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        drainCts.CancelAfter(TimeSpan.FromMilliseconds(100));
        try
        {
            await foreach (var _ in _queue.ReadAllAsync(drainCts.Token)) { }
        }
        catch (OperationCanceledException) { }
    }

    private async Task DelayOrDrain(TimeSpan delay, CancellationToken ct)
    {
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        delayCts.CancelAfter(delay);
        try
        {
            await foreach (var _ in _queue.ReadAllAsync(delayCts.Token)) { }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }
}
```

- [ ] **Step 3: Register the hosted service and HttpClient in DI**

In `DependencyInjection.cs`, add after the `SentinelAuditQueue` singleton registration (added in Task 4):

```csharp
        services.AddHttpClient("SentinelConnector");
        services.AddHostedService<SentinelConnectorWorker>();
```

- [ ] **Step 4: Verify build**

```bash
dotnet build PatchHound.slnx -v minimal
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/Services/SentinelConnectorWorker.cs \
  src/PatchHound.Infrastructure/DependencyInjection.cs \
  src/PatchHound.Infrastructure/PatchHound.Infrastructure.csproj
git commit -m "feat(sentinel): add SentinelConnectorWorker BackgroundService with MSAL auth and micro-batching"
```

---

### Task 6: IntegrationsController API endpoints

**Files:**
- Create: `src/PatchHound.Api/Controllers/IntegrationsController.cs`
- Create: `src/PatchHound.Api/Models/Integrations/SentinelConnectorDto.cs`
- Create: `src/PatchHound.Api/Models/Integrations/UpdateSentinelConnectorRequest.cs`

- [ ] **Step 1: Create the DTOs**

```csharp
// src/PatchHound.Api/Models/Integrations/SentinelConnectorDto.cs
namespace PatchHound.Api.Models.Integrations;

public record SentinelConnectorDto(
    bool Enabled,
    string DceEndpoint,
    string DcrImmutableId,
    string StreamName,
    string TenantId,
    string ClientId,
    bool HasSecret,
    DateTimeOffset? UpdatedAt
);
```

```csharp
// src/PatchHound.Api/Models/Integrations/UpdateSentinelConnectorRequest.cs
using System.ComponentModel.DataAnnotations;

namespace PatchHound.Api.Models.Integrations;

public record UpdateSentinelConnectorRequest(
    bool Enabled,
    [MaxLength(512)] string DceEndpoint,
    [MaxLength(256)] string DcrImmutableId,
    [MaxLength(256)] string StreamName,
    [MaxLength(128)] string TenantId,
    [MaxLength(128)] string ClientId,
    string? ClientSecret
);
```

- [ ] **Step 2: Create the controller**

```csharp
// src/PatchHound.Api/Controllers/IntegrationsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.Integrations;
using PatchHound.Core.Entities;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/integrations")]
[Authorize]
public class IntegrationsController : ControllerBase
{
    private const string VaultPath = "system/sentinel-connector";
    private const string VaultKey = "clientSecret";

    private readonly PatchHoundDbContext _dbContext;
    private readonly ISecretStore _secretStore;

    public IntegrationsController(PatchHoundDbContext dbContext, ISecretStore secretStore)
    {
        _dbContext = dbContext;
        _secretStore = secretStore;
    }

    [HttpGet("sentinel-connector")]
    [Authorize(Policy = Policies.ManageVault)]
    public async Task<ActionResult<SentinelConnectorDto>> GetSentinelConnector(
        CancellationToken ct
    )
    {
        var config = await _dbContext.SentinelConnectorConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (config is null)
        {
            return Ok(new SentinelConnectorDto(
                Enabled: false,
                DceEndpoint: string.Empty,
                DcrImmutableId: string.Empty,
                StreamName: string.Empty,
                TenantId: string.Empty,
                ClientId: string.Empty,
                HasSecret: false,
                UpdatedAt: null
            ));
        }

        return Ok(new SentinelConnectorDto(
            config.Enabled,
            config.DceEndpoint,
            config.DcrImmutableId,
            config.StreamName,
            config.TenantId,
            config.ClientId,
            HasSecret: !string.IsNullOrWhiteSpace(config.SecretRef),
            config.UpdatedAt
        ));
    }

    [HttpPut("sentinel-connector")]
    [Authorize(Policy = Policies.ManageVault)]
    public async Task<IActionResult> UpdateSentinelConnector(
        [FromBody] UpdateSentinelConnectorRequest request,
        CancellationToken ct
    )
    {
        var config = await _dbContext.SentinelConnectorConfigurations.FirstOrDefaultAsync(ct);
        var secretRef = config?.SecretRef ?? string.Empty;

        string? pendingSecretValue = null;
        if (!string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            secretRef = VaultPath;
            pendingSecretValue = request.ClientSecret.Trim();
        }

        if (config is null)
        {
            config = SentinelConnectorConfiguration.Create(
                request.Enabled,
                request.DceEndpoint,
                request.DcrImmutableId,
                request.StreamName,
                request.TenantId,
                request.ClientId,
                secretRef
            );
            await _dbContext.SentinelConnectorConfigurations.AddAsync(config, ct);
        }
        else
        {
            config.Update(
                request.Enabled,
                request.DceEndpoint,
                request.DcrImmutableId,
                request.StreamName,
                request.TenantId,
                request.ClientId,
                secretRef
            );
        }

        await _dbContext.SaveChangesAsync(ct);

        if (pendingSecretValue is not null)
        {
            await _secretStore.PutSecretAsync(
                VaultPath,
                new Dictionary<string, string> { [VaultKey] = pendingSecretValue },
                ct
            );
        }

        return NoContent();
    }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build PatchHound.slnx -v minimal
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Api/Controllers/IntegrationsController.cs \
  src/PatchHound.Api/Models/Integrations/
git commit -m "feat(sentinel): add IntegrationsController with GET/PUT sentinel-connector endpoints"
```

---

### Task 7: Frontend API layer — schemas and server functions

**Files:**
- Create: `frontend/src/api/integrations.schemas.ts`
- Create: `frontend/src/api/integrations.functions.ts`

- [ ] **Step 1: Create the Zod schemas**

```typescript
// frontend/src/api/integrations.schemas.ts
import { z } from 'zod'
import { isoDateTimeSchema } from './common.schemas'

export const sentinelConnectorSchema = z.object({
  enabled: z.boolean(),
  dceEndpoint: z.string(),
  dcrImmutableId: z.string(),
  streamName: z.string(),
  tenantId: z.string(),
  clientId: z.string(),
  hasSecret: z.boolean(),
  updatedAt: isoDateTimeSchema.nullable(),
})

export type SentinelConnectorConfig = z.infer<typeof sentinelConnectorSchema>

export const updateSentinelConnectorSchema = z.object({
  enabled: z.boolean(),
  dceEndpoint: z.string().max(512),
  dcrImmutableId: z.string().max(256),
  streamName: z.string().max(256),
  tenantId: z.string().max(128),
  clientId: z.string().max(128),
  clientSecret: z.string().nullable().optional(),
})

export type UpdateSentinelConnectorInput = z.infer<typeof updateSentinelConnectorSchema>
```

- [ ] **Step 2: Create the server functions**

```typescript
// frontend/src/api/integrations.functions.ts
import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPut } from '@/server/api'
import {
  sentinelConnectorSchema,
  updateSentinelConnectorSchema,
} from './integrations.schemas'

export const fetchSentinelConnector = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/integrations/sentinel-connector', context)
    return sentinelConnectorSchema.parse(data)
  })

export const updateSentinelConnector = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(updateSentinelConnectorSchema)
  .handler(async ({ context, data }) => {
    await apiPut('/integrations/sentinel-connector', context, data)
  })
```

- [ ] **Step 3: Verify frontend types**

```bash
cd /Users/frode.hus/src/github.com/frodehus/PatchHound/frontend && npm run typecheck
```

Expected: no type errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/integrations.schemas.ts frontend/src/api/integrations.functions.ts
git commit -m "feat(sentinel): add frontend API schemas and server functions for Sentinel connector"
```

---

### Task 8: Admin Integrations landing page

**Files:**
- Modify: `frontend/src/routes/_authed/admin/index.tsx:2,12,17-74` — add Integrations area
- Create: `frontend/src/routes/_authed/admin/integrations/index.tsx`

- [ ] **Step 1: Add Integrations to the admin areas**

In `frontend/src/routes/_authed/admin/index.tsx`:

Add `Plug` to the lucide-react imports on line 2:

```typescript
import { Building2, ChevronRight, DatabaseZap, GitBranchPlus, Plug, Settings2, ShieldCheck, ShieldEllipsis, Users, Workflow, Wrench } from 'lucide-react'
```

Update the `AdminArea` type `to` union on line 12 to include the new route:

```typescript
  to: '/admin/users' | '/admin/teams' | '/admin/tenants' | '/admin/sources' | '/admin/security-profiles' | '/admin/asset-rules' | '/admin/workflows' | '/admin/maintenance' | '/admin/integrations'
```

Add a new entry to the `adminAreas` array, before the Maintenance entry (before line 68):

```typescript
  {
    title: 'Integrations',
    description: 'Configure external service connectors such as Microsoft Sentinel for audit log forwarding.',
    to: '/admin/integrations',
    roles: ['GlobalAdmin'],
    icon: Plug,
  },
```

- [ ] **Step 2: Create the integrations route**

```tsx
// frontend/src/routes/_authed/admin/integrations/index.tsx
import { createFileRoute } from '@tanstack/react-router'
import { SentinelConnectorCard } from '@/components/features/admin/SentinelConnectorCard'

export const Route = createFileRoute('/_authed/admin/integrations/')({
  component: IntegrationsPage,
})

function IntegrationsPage() {
  return (
    <section className="space-y-5">
      <div className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
        <div className="space-y-2">
          <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
            Administration
          </p>
          <h1 className="text-3xl font-semibold tracking-[-0.04em]">
            Integrations
          </h1>
          <p className="max-w-2xl text-sm text-muted-foreground">
            Configure external service connectors for forwarding data to third-party platforms.
          </p>
        </div>
      </div>

      <SentinelConnectorCard />
    </section>
  )
}
```

- [ ] **Step 3: Create placeholder SentinelConnectorCard for type checking**

Create a minimal placeholder so the route compiles (full implementation in next task):

```tsx
// frontend/src/components/features/admin/SentinelConnectorCard.tsx
export function SentinelConnectorCard() {
  return <div>Sentinel connector placeholder</div>
}
```

- [ ] **Step 4: Verify frontend types and lint**

```bash
cd /Users/frode.hus/src/github.com/frodehus/PatchHound/frontend && npm run typecheck && npm run lint
```

Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/routes/_authed/admin/index.tsx \
  frontend/src/routes/_authed/admin/integrations/index.tsx \
  frontend/src/components/features/admin/SentinelConnectorCard.tsx
git commit -m "feat(sentinel): add Integrations admin area and route with placeholder card"
```

---

### Task 9: SentinelConnectorCard form component

**Files:**
- Modify: `frontend/src/components/features/admin/SentinelConnectorCard.tsx` — replace placeholder

- [ ] **Step 1: Implement the full component**

```tsx
// frontend/src/components/features/admin/SentinelConnectorCard.tsx
import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Satellite, Loader2 } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Badge } from '@/components/ui/badge'
import {
  fetchSentinelConnector,
  updateSentinelConnector,
} from '@/api/integrations.functions'
import type { UpdateSentinelConnectorInput } from '@/api/integrations.schemas'

export function SentinelConnectorCard() {
  const queryClient = useQueryClient()
  const { data: config, isLoading } = useQuery({
    queryKey: ['sentinel-connector'],
    queryFn: () => fetchSentinelConnector(),
  })

  const [editing, setEditing] = useState(false)

  if (isLoading || !config) {
    return (
      <Card className="rounded-2xl border-border/70">
        <CardContent className="flex items-center justify-center py-12">
          <Loader2 className="size-5 animate-spin text-muted-foreground" />
        </CardContent>
      </Card>
    )
  }

  const isConfigured = !!(config.dceEndpoint && config.dcrImmutableId && config.streamName)

  if (!editing && !isConfigured) {
    return (
      <Card className="rounded-2xl border-border/70">
        <CardHeader>
          <div className="flex items-center gap-3">
            <div className="rounded-2xl border border-border/70 bg-background/50 p-3">
              <Satellite className="size-5 text-primary" />
            </div>
            <div>
              <CardTitle>Microsoft Sentinel Data Connector</CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">
                Forward audit trail events to a Microsoft Sentinel workspace via the Logs Ingestion API.
              </p>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <p className="mb-4 text-sm text-muted-foreground">
            Not configured. You will need a Data Collection Endpoint, Data Collection Rule, and an Entra app registration with the Monitoring Metrics Publisher role.
          </p>
          <Button onClick={() => setEditing(true)}>Configure connector</Button>
        </CardContent>
      </Card>
    )
  }

  if (!editing) {
    return (
      <Card className="rounded-2xl border-border/70">
        <CardHeader>
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <div className="rounded-2xl border border-border/70 bg-background/50 p-3">
                <Satellite className="size-5 text-primary" />
              </div>
              <div>
                <CardTitle>Microsoft Sentinel Data Connector</CardTitle>
                <p className="mt-1 text-sm text-muted-foreground">
                  Audit event forwarding via Logs Ingestion API
                </p>
              </div>
            </div>
            <Badge variant={config.enabled ? 'default' : 'secondary'}>
              {config.enabled ? 'Enabled' : 'Disabled'}
            </Badge>
          </div>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="grid gap-2 text-sm sm:grid-cols-2">
            <div>
              <span className="text-muted-foreground">DCE Endpoint</span>
              <p className="truncate font-mono text-xs">{config.dceEndpoint}</p>
            </div>
            <div>
              <span className="text-muted-foreground">DCR Immutable ID</span>
              <p className="truncate font-mono text-xs">{config.dcrImmutableId}</p>
            </div>
            <div>
              <span className="text-muted-foreground">Stream Name</span>
              <p className="font-mono text-xs">{config.streamName}</p>
            </div>
            <div>
              <span className="text-muted-foreground">Client Secret</span>
              <p className="text-xs">{config.hasSecret ? '••••••••' : 'Not set'}</p>
            </div>
          </div>
          <Button variant="outline" onClick={() => setEditing(true)}>
            Edit configuration
          </Button>
        </CardContent>
      </Card>
    )
  }

  return (
    <SentinelConnectorForm
      config={config}
      onClose={() => setEditing(false)}
      onSaved={() => {
        queryClient.invalidateQueries({ queryKey: ['sentinel-connector'] })
        setEditing(false)
      }}
    />
  )
}

function SentinelConnectorForm({
  config,
  onClose,
  onSaved,
}: {
  config: { enabled: boolean; dceEndpoint: string; dcrImmutableId: string; streamName: string; tenantId: string; clientId: string; hasSecret: boolean }
  onClose: () => void
  onSaved: () => void
}) {
  const [form, setForm] = useState<UpdateSentinelConnectorInput>({
    enabled: config.enabled,
    dceEndpoint: config.dceEndpoint,
    dcrImmutableId: config.dcrImmutableId,
    streamName: config.streamName || 'Custom-PatchHoundAuditLog',
    tenantId: config.tenantId,
    clientId: config.clientId,
    clientSecret: null,
  })

  const mutation = useMutation({
    mutationFn: (data: UpdateSentinelConnectorInput) => updateSentinelConnector({ data }),
    onSuccess: () => onSaved(),
  })

  const update = (field: keyof UpdateSentinelConnectorInput, value: string | boolean | null) =>
    setForm((prev) => ({ ...prev, [field]: value }))

  return (
    <Card className="rounded-2xl border-border/70">
      <CardHeader>
        <div className="flex items-center gap-3">
          <div className="rounded-2xl border border-border/70 bg-background/50 p-3">
            <Satellite className="size-5 text-primary" />
          </div>
          <CardTitle>Microsoft Sentinel Data Connector</CardTitle>
        </div>
      </CardHeader>
      <CardContent className="space-y-5">
        <div className="flex items-center gap-3">
          <Switch
            id="sentinel-enabled"
            checked={form.enabled}
            onCheckedChange={(checked) => update('enabled', checked)}
          />
          <Label htmlFor="sentinel-enabled">Enable connector</Label>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="dce-endpoint">DCE Endpoint</Label>
            <Input
              id="dce-endpoint"
              placeholder="https://<name>.ingest.monitor.azure.com"
              value={form.dceEndpoint}
              onChange={(e) => update('dceEndpoint', e.target.value)}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="dcr-id">DCR Immutable ID</Label>
            <Input
              id="dcr-id"
              placeholder="dcr-xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
              value={form.dcrImmutableId}
              onChange={(e) => update('dcrImmutableId', e.target.value)}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="stream-name">Stream Name</Label>
            <Input
              id="stream-name"
              placeholder="Custom-PatchHoundAuditLog"
              value={form.streamName}
              onChange={(e) => update('streamName', e.target.value)}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="entra-tenant">Entra Tenant ID</Label>
            <Input
              id="entra-tenant"
              placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
              value={form.tenantId}
              onChange={(e) => update('tenantId', e.target.value)}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="client-id">Client ID</Label>
            <Input
              id="client-id"
              placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
              value={form.clientId}
              onChange={(e) => update('clientId', e.target.value)}
            />
          </div>
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="client-secret">
              Client Secret
              {config.hasSecret && (
                <span className="ml-2 text-xs text-muted-foreground">(leave blank to keep current)</span>
              )}
            </Label>
            <Input
              id="client-secret"
              type="password"
              placeholder={config.hasSecret ? '••••••••' : 'Enter client secret'}
              value={form.clientSecret ?? ''}
              onChange={(e) => update('clientSecret', e.target.value || null)}
            />
          </div>
        </div>

        <div className="flex gap-2">
          <Button
            onClick={() => mutation.mutate(form)}
            disabled={mutation.isPending}
          >
            {mutation.isPending && <Loader2 className="mr-2 size-4 animate-spin" />}
            Save
          </Button>
          <Button variant="outline" onClick={onClose} disabled={mutation.isPending}>
            Cancel
          </Button>
        </div>

        {mutation.isError && (
          <p className="text-sm text-destructive">
            {mutation.error instanceof Error ? mutation.error.message : 'Failed to save configuration'}
          </p>
        )}
      </CardContent>
    </Card>
  )
}
```

- [ ] **Step 2: Verify frontend types and lint**

```bash
cd /Users/frode.hus/src/github.com/frodehus/PatchHound/frontend && npm run typecheck && npm run lint
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/features/admin/SentinelConnectorCard.tsx
git commit -m "feat(sentinel): implement SentinelConnectorCard form component with view/edit states"
```

---

### Task 10: Full build verification and backend tests

**Files:**
- No new files — verification task

- [ ] **Step 1: Run full backend build and tests**

```bash
cd /Users/frode.hus/src/github.com/frodehus/PatchHound
dotnet build PatchHound.slnx -v minimal
dotnet test PatchHound.slnx -v minimal
```

Expected: build succeeds, all tests pass.

- [ ] **Step 2: Run frontend checks**

```bash
cd /Users/frode.hus/src/github.com/frodehus/PatchHound/frontend
npm run typecheck && npm run lint
```

Expected: no errors.

- [ ] **Step 3: Verify the EF migration applies cleanly (optional — requires running DB)**

```bash
cd /Users/frode.hus/src/github.com/frodehus/PatchHound
dotnet ef database update \
  --project src/PatchHound.Infrastructure \
  --startup-project src/PatchHound.Api
```

Expected: migration applied successfully.
