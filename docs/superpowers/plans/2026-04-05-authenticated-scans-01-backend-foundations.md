# Authenticated Scans — Plan 1: Backend Foundations

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the backend foundation for authenticated scans — new entities, EF migration, CRUD controllers, OpenBao secret handling, ingestion service with validation+staging+merge, scan-job dispatcher, and asset-rule "AssignScanProfile" operation. No scheduler, no runner binary, no UI in this plan.

**Architecture:** All new entities follow the existing pattern (private setters, static `Create`, update methods, EF configuration). Domain entities in `PatchHound.Core/Entities/`, EF configs in `PatchHound.Infrastructure/Data/Configurations/AuthenticatedScans/`, services in `PatchHound.Infrastructure/AuthenticatedScans/`, controllers in `PatchHound.Api/Controllers/`. One migration at the end adds all tables.

**Tech Stack:** .NET 8, EF Core, PostgreSQL, xUnit, OpenBao (existing `ISecretStore`), existing `ITenantContext` + `AuditLogWriter` patterns.

**Reference spec:** `docs/superpowers/specs/2026-04-05-authenticated-scans-design.md` (sections 4, 7).

---

## File Map

**New entities** (`src/PatchHound.Core/Entities/AuthenticatedScans/`):
- `ConnectionProfile.cs`
- `ScanRunner.cs`
- `ScanningTool.cs` + `ScanningToolVersion.cs`
- `ScanProfile.cs` + `ScanProfileTool.cs`
- `AssetScanProfileAssignment.cs`
- `AuthenticatedScanRun.cs`
- `ScanJob.cs` + `ScanJobResult.cs` + `ScanJobValidationIssue.cs`
- `StagedAuthenticatedScanSoftware.cs`

**Enum change** (`src/PatchHound.Core/Enums/SoftwareIdentitySourceSystem.cs`): add `AuthenticatedScan = 2`.

**EF configurations** (`src/PatchHound.Infrastructure/Data/Configurations/AuthenticatedScans/`): one per entity.

**DbSets** added to `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`.

**Services** (`src/PatchHound.Infrastructure/AuthenticatedScans/`):
- `AuthenticatedScanIngestionService.cs`
- `AuthenticatedScanOutputValidator.cs`
- `ScanJobDispatcher.cs`
- `ConnectionProfileSecretWriter.cs`

**Controllers** (`src/PatchHound.Api/Controllers/`):
- `ConnectionProfilesController.cs`
- `ScanRunnersController.cs`
- `ScanningToolsController.cs`
- `ScanProfilesController.cs`

**DTOs** (`src/PatchHound.Api/Models/AuthenticatedScans/`): request/response records per controller.

**Asset-rule integration** (`src/PatchHound.Core/Services/AssetRuleEvaluationService.cs` or wherever `IAssetRuleEvaluationService` lives): handle `"AssignScanProfile"` operation.

**Tests** (`tests/PatchHound.Tests/`):
- `Infrastructure/AuthenticatedScans/AuthenticatedScanIngestionServiceTests.cs`
- `Infrastructure/AuthenticatedScans/AuthenticatedScanOutputValidatorTests.cs`
- `Infrastructure/AuthenticatedScans/ScanJobDispatcherTests.cs`
- `Infrastructure/AuthenticatedScans/ScanningToolVersionRetentionTests.cs`
- `Api/ConnectionProfilesControllerTests.cs`
- `Api/ScanRunnersControllerTests.cs`
- `Api/ScanningToolsControllerTests.cs`
- `Api/ScanProfilesControllerTests.cs`
- `Core/AssetRuleEvaluationAssignScanProfileTests.cs`

**Migration** (`src/PatchHound.Infrastructure/Data/Migrations/`): `AddAuthenticatedScans`.

---

## Task 1: Add `AuthenticatedScan` to `SoftwareIdentitySourceSystem` enum

**Files:**
- Modify: `src/PatchHound.Core/Enums/SoftwareIdentitySourceSystem.cs`

- [ ] **Step 1: Edit the enum**

```csharp
namespace PatchHound.Core.Enums;

public enum SoftwareIdentitySourceSystem
{
    Defender = 1,
    AuthenticatedScan = 2,
}
```

- [ ] **Step 2: Build**

Run: `dotnet build PatchHound.slnx`
Expected: build succeeds (this is an additive enum value, no consumers need updates yet).

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Core/Enums/SoftwareIdentitySourceSystem.cs
git commit -m "feat: add AuthenticatedScan source system"
```

---

## Task 2: `ConnectionProfile` entity

**Files:**
- Create: `src/PatchHound.Core/Entities/AuthenticatedScans/ConnectionProfile.cs`

- [ ] **Step 1: Write the entity**

```csharp
namespace PatchHound.Core.Entities.AuthenticatedScans;

public class ConnectionProfile
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Kind { get; private set; } = "ssh";
    public string SshHost { get; private set; } = string.Empty;
    public int SshPort { get; private set; } = 22;
    public string SshUsername { get; private set; } = string.Empty;
    public string AuthMethod { get; private set; } = "password"; // "password" | "privateKey"
    public string SecretRef { get; private set; } = string.Empty;
    public string? HostKeyFingerprint { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ConnectionProfile() { }

    public static ConnectionProfile Create(
        Guid tenantId,
        string name,
        string description,
        string sshHost,
        int sshPort,
        string sshUsername,
        string authMethod,
        string secretRef,
        string? hostKeyFingerprint)
    {
        if (authMethod is not ("password" or "privateKey"))
            throw new ArgumentException("authMethod must be 'password' or 'privateKey'", nameof(authMethod));
        var now = DateTimeOffset.UtcNow;
        return new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Description = description.Trim(),
            Kind = "ssh",
            SshHost = sshHost.Trim(),
            SshPort = sshPort,
            SshUsername = sshUsername.Trim(),
            AuthMethod = authMethod,
            SecretRef = secretRef,
            HostKeyFingerprint = string.IsNullOrWhiteSpace(hostKeyFingerprint) ? null : hostKeyFingerprint.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(
        string name,
        string description,
        string sshHost,
        int sshPort,
        string sshUsername,
        string authMethod,
        string? hostKeyFingerprint)
    {
        if (authMethod is not ("password" or "privateKey"))
            throw new ArgumentException("authMethod must be 'password' or 'privateKey'", nameof(authMethod));
        Name = name.Trim();
        Description = description.Trim();
        SshHost = sshHost.Trim();
        SshPort = sshPort;
        SshUsername = sshUsername.Trim();
        AuthMethod = authMethod;
        HostKeyFingerprint = string.IsNullOrWhiteSpace(hostKeyFingerprint) ? null : hostKeyFingerprint.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetSecretRef(string secretRef)
    {
        SecretRef = secretRef;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/PatchHound.Core`
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Core/Entities/AuthenticatedScans/ConnectionProfile.cs
git commit -m "feat: add ConnectionProfile entity"
```

---

## Task 3: `ScanRunner` entity

**Files:**
- Create: `src/PatchHound.Core/Entities/AuthenticatedScans/ScanRunner.cs`

- [ ] **Step 1: Write the entity**

```csharp
namespace PatchHound.Core.Entities.AuthenticatedScans;

public class ScanRunner
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string SecretHash { get; private set; } = string.Empty;
    public DateTimeOffset? LastSeenAt { get; private set; }
    public string Version { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ScanRunner() { }

    public static ScanRunner Create(Guid tenantId, string name, string description, string secretHash)
    {
        var now = DateTimeOffset.UtcNow;
        return new ScanRunner
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Description = description.Trim(),
            SecretHash = secretHash,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string name, string description, bool enabled)
    {
        Name = name.Trim();
        Description = description.Trim();
        Enabled = enabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RotateSecret(string newHash)
    {
        SecretHash = newHash;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordHeartbeat(string version, DateTimeOffset at)
    {
        Version = version.Trim();
        LastSeenAt = at;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/PatchHound.Core`
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/PatchHound.Core/Entities/AuthenticatedScans/ScanRunner.cs
git commit -m "feat: add ScanRunner entity"
```

---

## Task 4: `ScanningTool` + `ScanningToolVersion` entities

**Files:**
- Create: `src/PatchHound.Core/Entities/AuthenticatedScans/ScanningTool.cs`
- Create: `src/PatchHound.Core/Entities/AuthenticatedScans/ScanningToolVersion.cs`

- [ ] **Step 1: Write `ScanningToolVersion`**

```csharp
namespace PatchHound.Core.Entities.AuthenticatedScans;

public class ScanningToolVersion
{
    public Guid Id { get; private set; }
    public Guid ScanningToolId { get; private set; }
    public int VersionNumber { get; private set; }
    public string ScriptContent { get; private set; } = string.Empty;
    public Guid EditedByUserId { get; private set; }
    public DateTimeOffset EditedAt { get; private set; }

    private ScanningToolVersion() { }

    public static ScanningToolVersion Create(
        Guid scanningToolId,
        int versionNumber,
        string scriptContent,
        Guid editedByUserId)
    {
        return new ScanningToolVersion
        {
            Id = Guid.NewGuid(),
            ScanningToolId = scanningToolId,
            VersionNumber = versionNumber,
            ScriptContent = scriptContent,
            EditedByUserId = editedByUserId,
            EditedAt = DateTimeOffset.UtcNow,
        };
    }
}
```

- [ ] **Step 2: Write `ScanningTool`**

```csharp
namespace PatchHound.Core.Entities.AuthenticatedScans;

public class ScanningTool
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string ScriptType { get; private set; } = "python";   // "python"|"bash"|"powershell"
    public string InterpreterPath { get; private set; } = string.Empty;
    public int TimeoutSeconds { get; private set; } = 300;
    public string OutputModel { get; private set; } = "NormalizedSoftware";
    public Guid? CurrentVersionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ScanningTool() { }

    public static ScanningTool Create(
        Guid tenantId,
        string name,
        string description,
        string scriptType,
        string interpreterPath,
        int timeoutSeconds,
        string outputModel)
    {
        ValidateScriptType(scriptType);
        if (outputModel != "NormalizedSoftware")
            throw new ArgumentException("outputModel must be 'NormalizedSoftware' in v1", nameof(outputModel));
        if (timeoutSeconds is < 5 or > 3600)
            throw new ArgumentException("timeoutSeconds must be between 5 and 3600", nameof(timeoutSeconds));
        var now = DateTimeOffset.UtcNow;
        return new ScanningTool
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Description = description.Trim(),
            ScriptType = scriptType,
            InterpreterPath = interpreterPath.Trim(),
            TimeoutSeconds = timeoutSeconds,
            OutputModel = outputModel,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void UpdateMetadata(string name, string description, string scriptType, string interpreterPath, int timeoutSeconds)
    {
        ValidateScriptType(scriptType);
        if (timeoutSeconds is < 5 or > 3600)
            throw new ArgumentException("timeoutSeconds must be between 5 and 3600", nameof(timeoutSeconds));
        Name = name.Trim();
        Description = description.Trim();
        ScriptType = scriptType;
        InterpreterPath = interpreterPath.Trim();
        TimeoutSeconds = timeoutSeconds;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCurrentVersion(Guid versionId)
    {
        CurrentVersionId = versionId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ValidateScriptType(string value)
    {
        if (value is not ("python" or "bash" or "powershell"))
            throw new ArgumentException("scriptType must be python|bash|powershell", nameof(value));
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/PatchHound.Core`
Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Core/Entities/AuthenticatedScans/ScanningTool.cs src/PatchHound.Core/Entities/AuthenticatedScans/ScanningToolVersion.cs
git commit -m "feat: add ScanningTool and ScanningToolVersion entities"
```

---

## Task 5: `ScanProfile`, `ScanProfileTool`, `AssetScanProfileAssignment` entities

**Files:**
- Create: `src/PatchHound.Core/Entities/AuthenticatedScans/ScanProfile.cs`
- Create: `src/PatchHound.Core/Entities/AuthenticatedScans/ScanProfileTool.cs`
- Create: `src/PatchHound.Core/Entities/AuthenticatedScans/AssetScanProfileAssignment.cs`

- [ ] **Step 1: Write `ScanProfile`**

```csharp
namespace PatchHound.Core.Entities.AuthenticatedScans;

public class ScanProfile
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string CronSchedule { get; private set; } = string.Empty; // "" = manual only
    public Guid ConnectionProfileId { get; private set; }
    public Guid ScanRunnerId { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset? ManualRequestedAt { get; private set; }
    public DateTimeOffset? LastRunStartedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ScanProfile() { }

    public static ScanProfile Create(
        Guid tenantId,
        string name,
        string description,
        string cronSchedule,
        Guid connectionProfileId,
        Guid scanRunnerId,
        bool enabled)
    {
        var now = DateTimeOffset.UtcNow;
        return new ScanProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Description = description.Trim(),
            CronSchedule = cronSchedule.Trim(),
            ConnectionProfileId = connectionProfileId,
            ScanRunnerId = scanRunnerId,
            Enabled = enabled,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string name, string description, string cronSchedule,
                       Guid connectionProfileId, Guid scanRunnerId, bool enabled)
    {
        Name = name.Trim();
        Description = description.Trim();
        CronSchedule = cronSchedule.Trim();
        ConnectionProfileId = connectionProfileId;
        ScanRunnerId = scanRunnerId;
        Enabled = enabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RequestManualRun(DateTimeOffset at)
    {
        ManualRequestedAt = at;
        UpdatedAt = at;
    }

    public void ClearManualRequest()
    {
        ManualRequestedAt = null;
    }

    public void RecordRunStarted(DateTimeOffset at)
    {
        LastRunStartedAt = at;
    }
}
```

- [ ] **Step 2: Write `ScanProfileTool` (join table)**

```csharp
namespace PatchHound.Core.Entities.AuthenticatedScans;

public class ScanProfileTool
{
    public Guid ScanProfileId { get; private set; }
    public Guid ScanningToolId { get; private set; }
    public int ExecutionOrder { get; private set; }

    private ScanProfileTool() { }

    public static ScanProfileTool Create(Guid scanProfileId, Guid scanningToolId, int executionOrder) =>
        new() { ScanProfileId = scanProfileId, ScanningToolId = scanningToolId, ExecutionOrder = executionOrder };
}
```

- [ ] **Step 3: Write `AssetScanProfileAssignment`**

```csharp
namespace PatchHound.Core.Entities.AuthenticatedScans;

public class AssetScanProfileAssignment
{
    public Guid TenantId { get; private set; }
    public Guid AssetId { get; private set; }
    public Guid ScanProfileId { get; private set; }
    public Guid? AssignedByRuleId { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }

    private AssetScanProfileAssignment() { }

    public static AssetScanProfileAssignment Create(Guid tenantId, Guid assetId, Guid scanProfileId, Guid? assignedByRuleId) =>
        new()
        {
            TenantId = tenantId,
            AssetId = assetId,
            ScanProfileId = scanProfileId,
            AssignedByRuleId = assignedByRuleId,
            AssignedAt = DateTimeOffset.UtcNow,
        };
}
```

- [ ] **Step 4: Build & commit**

```bash
dotnet build src/PatchHound.Core
git add src/PatchHound.Core/Entities/AuthenticatedScans/ScanProfile.cs src/PatchHound.Core/Entities/AuthenticatedScans/ScanProfileTool.cs src/PatchHound.Core/Entities/AuthenticatedScans/AssetScanProfileAssignment.cs
git commit -m "feat: add ScanProfile, ScanProfileTool, AssetScanProfileAssignment entities"
```

---

## Task 6: Run/Job entities and staging table

**Files:**
- Create: `src/PatchHound.Core/Entities/AuthenticatedScans/AuthenticatedScanRun.cs`
- Create: `src/PatchHound.Core/Entities/AuthenticatedScans/ScanJob.cs`
- Create: `src/PatchHound.Core/Entities/AuthenticatedScans/ScanJobResult.cs`
- Create: `src/PatchHound.Core/Entities/AuthenticatedScans/ScanJobValidationIssue.cs`
- Create: `src/PatchHound.Core/Entities/AuthenticatedScans/StagedAuthenticatedScanSoftware.cs`

- [ ] **Step 1: Write `AuthenticatedScanRun`**

```csharp
namespace PatchHound.Core.Entities.AuthenticatedScans;

public static class AuthenticatedScanRunStatuses
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string PartiallyFailed = "PartiallyFailed";
    public const string Failed = "Failed";
}

public class AuthenticatedScanRun
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ScanProfileId { get; private set; }
    public string TriggerKind { get; private set; } = "scheduled"; // "scheduled"|"manual"
    public Guid? TriggeredByUserId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string Status { get; private set; } = AuthenticatedScanRunStatuses.Queued;
    public int TotalDevices { get; private set; }
    public int SucceededCount { get; private set; }
    public int FailedCount { get; private set; }
    public int EntriesIngested { get; private set; }

    private AuthenticatedScanRun() { }

    public static AuthenticatedScanRun Start(
        Guid tenantId, Guid scanProfileId, string triggerKind, Guid? triggeredByUserId, DateTimeOffset at)
    {
        return new AuthenticatedScanRun
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ScanProfileId = scanProfileId,
            TriggerKind = triggerKind,
            TriggeredByUserId = triggeredByUserId,
            StartedAt = at,
            Status = AuthenticatedScanRunStatuses.Queued,
        };
    }

    public void MarkRunning(int totalDevices)
    {
        TotalDevices = totalDevices;
        Status = AuthenticatedScanRunStatuses.Running;
    }

    public void Complete(int succeeded, int failed, int entriesIngested, DateTimeOffset at)
    {
        SucceededCount = succeeded;
        FailedCount = failed;
        EntriesIngested = entriesIngested;
        CompletedAt = at;
        Status = (failed, succeeded) switch
        {
            (0, _) => AuthenticatedScanRunStatuses.Succeeded,
            (_, 0) => AuthenticatedScanRunStatuses.Failed,
            _      => AuthenticatedScanRunStatuses.PartiallyFailed,
        };
    }
}
```

- [ ] **Step 2: Write `ScanJob`**

```csharp
namespace PatchHound.Core.Entities.AuthenticatedScans;

public static class ScanJobStatuses
{
    public const string Pending = "Pending";
    public const string Dispatched = "Dispatched";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string TimedOut = "TimedOut";
}

public class ScanJob
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RunId { get; private set; }
    public Guid ScanRunnerId { get; private set; }
    public Guid AssetId { get; private set; }
    public Guid ConnectionProfileId { get; private set; }
    public string ScanningToolVersionIdsJson { get; private set; } = "[]";
    public string Status { get; private set; } = ScanJobStatuses.Pending;
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;
    public int StdoutBytes { get; private set; }
    public int StderrBytes { get; private set; }
    public int EntriesIngested { get; private set; }

    private ScanJob() { }

    public static ScanJob Create(
        Guid tenantId, Guid runId, Guid scanRunnerId, Guid assetId,
        Guid connectionProfileId, string scanningToolVersionIdsJson)
    {
        return new ScanJob
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RunId = runId,
            ScanRunnerId = scanRunnerId,
            AssetId = assetId,
            ConnectionProfileId = connectionProfileId,
            ScanningToolVersionIdsJson = scanningToolVersionIdsJson,
            Status = ScanJobStatuses.Pending,
        };
    }

    public void Dispatch(DateTimeOffset leaseExpiresAt)
    {
        Status = ScanJobStatuses.Dispatched;
        LeaseExpiresAt = leaseExpiresAt;
        AttemptCount++;
    }

    public void ReturnToPending(string reason)
    {
        Status = ScanJobStatuses.Pending;
        LeaseExpiresAt = null;
        ErrorMessage = reason;
    }

    public void CompleteSucceeded(int stdoutBytes, int stderrBytes, int entriesIngested, DateTimeOffset at)
    {
        Status = ScanJobStatuses.Succeeded;
        StdoutBytes = stdoutBytes;
        StderrBytes = stderrBytes;
        EntriesIngested = entriesIngested;
        CompletedAt = at;
    }

    public void CompleteFailed(string status, string errorMessage, DateTimeOffset at)
    {
        Status = status; // Failed | TimedOut
        ErrorMessage = errorMessage;
        CompletedAt = at;
    }
}
```

- [ ] **Step 3: Write `ScanJobResult`**

```csharp
namespace PatchHound.Core.Entities.AuthenticatedScans;

public class ScanJobResult
{
    public Guid Id { get; private set; }
    public Guid ScanJobId { get; private set; }
    public string RawStdout { get; private set; } = string.Empty;
    public string RawStderr { get; private set; } = string.Empty;
    public string ParsedJson { get; private set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; private set; }

    private ScanJobResult() { }

    public static ScanJobResult Create(Guid scanJobId, string rawStdout, string rawStderr, string parsedJson) =>
        new()
        {
            Id = Guid.NewGuid(),
            ScanJobId = scanJobId,
            RawStdout = rawStdout,
            RawStderr = rawStderr,
            ParsedJson = parsedJson,
            CapturedAt = DateTimeOffset.UtcNow,
        };
}
```

- [ ] **Step 4: Write `ScanJobValidationIssue`**

```csharp
namespace PatchHound.Core.Entities.AuthenticatedScans;

public class ScanJobValidationIssue
{
    public Guid Id { get; private set; }
    public Guid ScanJobId { get; private set; }
    public string FieldPath { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public int EntryIndex { get; private set; }

    private ScanJobValidationIssue() { }

    public static ScanJobValidationIssue Create(Guid scanJobId, string fieldPath, string message, int entryIndex) =>
        new()
        {
            Id = Guid.NewGuid(),
            ScanJobId = scanJobId,
            FieldPath = fieldPath,
            Message = message,
            EntryIndex = entryIndex,
        };
}
```

- [ ] **Step 5: Write `StagedAuthenticatedScanSoftware`**

```csharp
namespace PatchHound.Core.Entities.AuthenticatedScans;

public class StagedAuthenticatedScanSoftware
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ScanJobId { get; private set; }
    public Guid DeviceAssetId { get; private set; }
    public Guid ScanProfileId { get; private set; }
    public string CanonicalName { get; private set; } = string.Empty;
    public string CanonicalProductKey { get; private set; } = string.Empty;
    public string? CanonicalVendor { get; private set; }
    public string? Category { get; private set; }
    public string? PrimaryCpe23Uri { get; private set; }
    public string? DetectedVersion { get; private set; }
    public DateTimeOffset StagedAt { get; private set; }

    private StagedAuthenticatedScanSoftware() { }

    public static StagedAuthenticatedScanSoftware Create(
        Guid tenantId, Guid scanJobId, Guid deviceAssetId, Guid scanProfileId,
        string canonicalName, string canonicalProductKey,
        string? canonicalVendor, string? category, string? primaryCpe23Uri, string? detectedVersion) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ScanJobId = scanJobId,
            DeviceAssetId = deviceAssetId,
            ScanProfileId = scanProfileId,
            CanonicalName = canonicalName.Trim(),
            CanonicalProductKey = canonicalProductKey.Trim(),
            CanonicalVendor = string.IsNullOrWhiteSpace(canonicalVendor) ? null : canonicalVendor.Trim(),
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            PrimaryCpe23Uri = string.IsNullOrWhiteSpace(primaryCpe23Uri) ? null : primaryCpe23Uri.Trim(),
            DetectedVersion = string.IsNullOrWhiteSpace(detectedVersion) ? null : detectedVersion.Trim(),
            StagedAt = DateTimeOffset.UtcNow,
        };
}
```

- [ ] **Step 6: Build & commit**

```bash
dotnet build src/PatchHound.Core
git add src/PatchHound.Core/Entities/AuthenticatedScans/
git commit -m "feat: add scan run, job, result, issue, and staging entities"
```

---

## Task 7: EF configurations + register DbSets

**Files:**
- Create: `src/PatchHound.Infrastructure/Data/Configurations/AuthenticatedScans/` (11 files)
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`

- [ ] **Step 1: Create all configurations**

Create one file per entity following the `AdvancedToolConfiguration` pattern. Full contents:

`ConnectionProfileConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class ConnectionProfileConfiguration : IEntityTypeConfiguration<ConnectionProfile>
{
    public void Configure(EntityTypeBuilder<ConnectionProfile> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.Description).HasColumnType("text").IsRequired();
        b.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        b.Property(x => x.SshHost).HasMaxLength(512).IsRequired();
        b.Property(x => x.SshUsername).HasMaxLength(256).IsRequired();
        b.Property(x => x.AuthMethod).HasMaxLength(32).IsRequired();
        b.Property(x => x.SecretRef).HasMaxLength(512).IsRequired();
        b.Property(x => x.HostKeyFingerprint).HasMaxLength(128);
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}
```

`ScanRunnerConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class ScanRunnerConfiguration : IEntityTypeConfiguration<ScanRunner>
{
    public void Configure(EntityTypeBuilder<ScanRunner> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.Description).HasColumnType("text").IsRequired();
        b.Property(x => x.SecretHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.Version).HasMaxLength(128).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}
```

`ScanningToolConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class ScanningToolConfiguration : IEntityTypeConfiguration<ScanningTool>
{
    public void Configure(EntityTypeBuilder<ScanningTool> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.Description).HasColumnType("text").IsRequired();
        b.Property(x => x.ScriptType).HasMaxLength(32).IsRequired();
        b.Property(x => x.InterpreterPath).HasMaxLength(512).IsRequired();
        b.Property(x => x.OutputModel).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}
```

`ScanningToolVersionConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class ScanningToolVersionConfiguration : IEntityTypeConfiguration<ScanningToolVersion>
{
    public void Configure(EntityTypeBuilder<ScanningToolVersion> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.ScriptContent).HasColumnType("text").IsRequired();
        b.HasIndex(x => new { x.ScanningToolId, x.VersionNumber }).IsUnique();
    }
}
```

`ScanProfileConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class ScanProfileConfiguration : IEntityTypeConfiguration<ScanProfile>
{
    public void Configure(EntityTypeBuilder<ScanProfile> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.Description).HasColumnType("text").IsRequired();
        b.Property(x => x.CronSchedule).HasMaxLength(128).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}
```

`ScanProfileToolConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class ScanProfileToolConfiguration : IEntityTypeConfiguration<ScanProfileTool>
{
    public void Configure(EntityTypeBuilder<ScanProfileTool> b)
    {
        b.HasKey(x => new { x.ScanProfileId, x.ScanningToolId });
    }
}
```

`AssetScanProfileAssignmentConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class AssetScanProfileAssignmentConfiguration : IEntityTypeConfiguration<AssetScanProfileAssignment>
{
    public void Configure(EntityTypeBuilder<AssetScanProfileAssignment> b)
    {
        b.HasKey(x => new { x.AssetId, x.ScanProfileId });
        b.HasIndex(x => new { x.TenantId, x.ScanProfileId });
    }
}
```

`AuthenticatedScanRunConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class AuthenticatedScanRunConfiguration : IEntityTypeConfiguration<AuthenticatedScanRun>
{
    public void Configure(EntityTypeBuilder<AuthenticatedScanRun> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.TriggerKind).HasMaxLength(32).IsRequired();
        b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.ScanProfileId, x.StartedAt });
    }
}
```

`ScanJobConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class ScanJobConfiguration : IEntityTypeConfiguration<ScanJob>
{
    public void Configure(EntityTypeBuilder<ScanJob> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.ScanningToolVersionIdsJson).HasColumnType("text").IsRequired();
        b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.Property(x => x.ErrorMessage).HasColumnType("text").IsRequired();
        b.HasIndex(x => new { x.ScanRunnerId, x.Status });
        b.HasIndex(x => x.RunId);
    }
}
```

`ScanJobResultConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class ScanJobResultConfiguration : IEntityTypeConfiguration<ScanJobResult>
{
    public void Configure(EntityTypeBuilder<ScanJobResult> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.RawStdout).HasColumnType("text").IsRequired();
        b.Property(x => x.RawStderr).HasColumnType("text").IsRequired();
        b.Property(x => x.ParsedJson).HasColumnType("text").IsRequired();
        b.HasIndex(x => x.ScanJobId).IsUnique();
    }
}
```

`ScanJobValidationIssueConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class ScanJobValidationIssueConfiguration : IEntityTypeConfiguration<ScanJobValidationIssue>
{
    public void Configure(EntityTypeBuilder<ScanJobValidationIssue> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.FieldPath).HasMaxLength(256).IsRequired();
        b.Property(x => x.Message).HasColumnType("text").IsRequired();
        b.HasIndex(x => x.ScanJobId);
    }
}
```

`StagedAuthenticatedScanSoftwareConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities.AuthenticatedScans;

namespace PatchHound.Infrastructure.Data.Configurations.AuthenticatedScans;

public class StagedAuthenticatedScanSoftwareConfiguration : IEntityTypeConfiguration<StagedAuthenticatedScanSoftware>
{
    public void Configure(EntityTypeBuilder<StagedAuthenticatedScanSoftware> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.CanonicalName).HasMaxLength(1024).IsRequired();
        b.Property(x => x.CanonicalProductKey).HasMaxLength(1024).IsRequired();
        b.Property(x => x.CanonicalVendor).HasMaxLength(1024);
        b.Property(x => x.Category).HasMaxLength(1024);
        b.Property(x => x.PrimaryCpe23Uri).HasMaxLength(1024);
        b.Property(x => x.DetectedVersion).HasMaxLength(256);
        b.HasIndex(x => x.ScanJobId);
    }
}
```

- [ ] **Step 2: Register DbSets in `PatchHoundDbContext`**

Add `using PatchHound.Core.Entities.AuthenticatedScans;` at the top and add these DbSets after the existing ones:

```csharp
public DbSet<ConnectionProfile> ConnectionProfiles => Set<ConnectionProfile>();
public DbSet<ScanRunner> ScanRunners => Set<ScanRunner>();
public DbSet<ScanningTool> ScanningTools => Set<ScanningTool>();
public DbSet<ScanningToolVersion> ScanningToolVersions => Set<ScanningToolVersion>();
public DbSet<ScanProfile> ScanProfiles => Set<ScanProfile>();
public DbSet<ScanProfileTool> ScanProfileTools => Set<ScanProfileTool>();
public DbSet<AssetScanProfileAssignment> AssetScanProfileAssignments => Set<AssetScanProfileAssignment>();
public DbSet<AuthenticatedScanRun> AuthenticatedScanRuns => Set<AuthenticatedScanRun>();
public DbSet<ScanJob> ScanJobs => Set<ScanJob>();
public DbSet<ScanJobResult> ScanJobResults => Set<ScanJobResult>();
public DbSet<ScanJobValidationIssue> ScanJobValidationIssues => Set<ScanJobValidationIssue>();
public DbSet<StagedAuthenticatedScanSoftware> StagedAuthenticatedScanSoftware => Set<StagedAuthenticatedScanSoftware>();
```

Confirm configurations auto-register: check `OnModelCreating` in `PatchHoundDbContext.cs`. If it calls `modelBuilder.ApplyConfigurationsFromAssembly(...)` the new configs pick up automatically. If not, add `modelBuilder.ApplyConfiguration(new ConnectionProfileConfiguration());` etc. (inspect existing `OnModelCreating` to match style).

- [ ] **Step 3: Build**

Run: `dotnet build PatchHound.slnx`
Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/Data/Configurations/AuthenticatedScans/ src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs
git commit -m "feat: add EF configurations and DbSets for authenticated scans"
```

---

## Task 8: EF migration

**Files:**
- Create: `src/PatchHound.Infrastructure/Data/Migrations/*_AddAuthenticatedScans.cs` (generated)

- [ ] **Step 1: Generate migration**

Run:
```bash
cd src/PatchHound.Infrastructure
dotnet ef migrations add AddAuthenticatedScans --startup-project ../PatchHound.Api
```

Expected: migration files created under `Data/Migrations/`.

- [ ] **Step 2: Inspect migration**

Open the newly generated `*_AddAuthenticatedScans.cs`. Confirm all 12 new tables are created with expected columns, indexes, and FK constraints.

- [ ] **Step 3: Apply migration to test DB**

Run:
```bash
dotnet ef database update --startup-project ../PatchHound.Api
```

Expected: migration applies without error.

- [ ] **Step 4: Commit**

```bash
cd ../..
git add src/PatchHound.Infrastructure/Data/Migrations/
git commit -m "feat: add migration for authenticated scans tables"
```

---

## Task 9: `ConnectionProfileSecretWriter` — OpenBao writes

**Files:**
- Create: `src/PatchHound.Infrastructure/AuthenticatedScans/ConnectionProfileSecretWriter.cs`

Stores SSH credentials in OpenBao at `tenants/{tenantId}/auth-scan-connections/{profileId}` using the existing `ISecretStore`.

- [ ] **Step 1: Write the class**

```csharp
using PatchHound.Infrastructure.Secrets;

namespace PatchHound.Infrastructure.AuthenticatedScans;

public class ConnectionProfileSecretWriter
{
    private readonly ISecretStore _secretStore;

    public ConnectionProfileSecretWriter(ISecretStore secretStore)
    {
        _secretStore = secretStore;
    }

    public static string BuildPath(Guid tenantId, Guid profileId) =>
        $"tenants/{tenantId}/auth-scan-connections/{profileId}";

    public async Task<string> WritePasswordAsync(Guid tenantId, Guid profileId, string password, CancellationToken ct)
    {
        var path = BuildPath(tenantId, profileId);
        await _secretStore.PutSecretAsync(path, new Dictionary<string, string> { ["password"] = password }, ct);
        return path;
    }

    public async Task<string> WritePrivateKeyAsync(
        Guid tenantId, Guid profileId, string privateKey, string? passphrase, CancellationToken ct)
    {
        var path = BuildPath(tenantId, profileId);
        var dict = new Dictionary<string, string> { ["privateKey"] = privateKey };
        if (!string.IsNullOrEmpty(passphrase)) dict["passphrase"] = passphrase;
        await _secretStore.PutSecretAsync(path, dict, ct);
        return path;
    }

    public async Task DeleteAsync(Guid tenantId, Guid profileId, CancellationToken ct)
    {
        await _secretStore.DeleteSecretPathAsync(BuildPath(tenantId, profileId), ct);
    }
}
```

- [ ] **Step 2: Register in DI**

Find `src/PatchHound.Infrastructure/DependencyInjection.cs`. Add `services.AddScoped<ConnectionProfileSecretWriter>();` in the appropriate extension method.

- [ ] **Step 3: Build**

Run: `dotnet build PatchHound.slnx`
Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/PatchHound.Infrastructure/AuthenticatedScans/ConnectionProfileSecretWriter.cs src/PatchHound.Infrastructure/DependencyInjection.cs
git commit -m "feat: add ConnectionProfileSecretWriter for OpenBao secret handling"
```

---

## Task 10: `AuthenticatedScanOutputValidator` with tests (TDD)

**Files:**
- Create: `src/PatchHound.Infrastructure/AuthenticatedScans/AuthenticatedScanOutputValidator.cs`
- Create: `tests/PatchHound.Tests/Infrastructure/AuthenticatedScans/AuthenticatedScanOutputValidatorTests.cs`

Validates parsed JSON per §7.1/§7.2 of the spec.

- [ ] **Step 1: Write failing tests**

```csharp
using PatchHound.Infrastructure.AuthenticatedScans;
using Xunit;

namespace PatchHound.Tests.Infrastructure.AuthenticatedScans;

public class AuthenticatedScanOutputValidatorTests
{
    private readonly AuthenticatedScanOutputValidator _sut = new();

    [Fact]
    public void Validate_returns_all_entries_when_valid()
    {
        var json = """{"software":[{"canonicalName":"nginx","canonicalProductKey":"nginx:nginx","detectedVersion":"1.24.0"}]}""";
        var result = _sut.Validate(json);
        Assert.Empty(result.Issues);
        Assert.Single(result.ValidEntries);
        Assert.Equal("nginx", result.ValidEntries[0].CanonicalName);
        Assert.Equal("1.24.0", result.ValidEntries[0].DetectedVersion);
    }

    [Fact]
    public void Validate_flags_missing_required_fields_and_keeps_valid_entries()
    {
        var json = """
        {"software":[
          {"canonicalName":"nginx","canonicalProductKey":"nginx:nginx"},
          {"canonicalName":""},
          {"canonicalProductKey":"acme:foo"}
        ]}""";
        var result = _sut.Validate(json);
        Assert.Single(result.ValidEntries);
        Assert.Equal(2, result.Issues.Count);
        Assert.Contains(result.Issues, i => i.EntryIndex == 1 && i.FieldPath.Contains("canonicalName"));
        Assert.Contains(result.Issues, i => i.EntryIndex == 2 && i.FieldPath.Contains("canonicalName"));
    }

    [Fact]
    public void Validate_rejects_missing_software_array()
    {
        var json = """{"notSoftware":[]}""";
        var result = _sut.Validate(json);
        Assert.True(result.FatalError);
        Assert.Contains("software", result.FatalErrorMessage);
    }

    [Fact]
    public void Validate_rejects_non_array_software()
    {
        var result = _sut.Validate("""{"software":"not-an-array"}""");
        Assert.True(result.FatalError);
    }

    [Fact]
    public void Validate_rejects_entry_count_over_5000()
    {
        var entries = string.Join(",", Enumerable.Range(0, 5001)
            .Select(i => $$"""{"canonicalName":"a","canonicalProductKey":"k{{i}}"}"""));
        var json = $$"""{"software":[{{entries}}]}""";
        var result = _sut.Validate(json);
        Assert.True(result.FatalError);
        Assert.Contains("5000", result.FatalErrorMessage);
    }

    [Fact]
    public void Validate_rejects_string_over_1024_chars()
    {
        var longStr = new string('x', 1025);
        var json = $$"""{"software":[{"canonicalName":"{{longStr}}","canonicalProductKey":"k"}]}""";
        var result = _sut.Validate(json);
        Assert.Empty(result.ValidEntries);
        Assert.Single(result.Issues);
        Assert.Contains("1024", result.Issues[0].Message);
    }

    [Fact]
    public void Validate_rejects_invalid_json()
    {
        var result = _sut.Validate("not json");
        Assert.True(result.FatalError);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~AuthenticatedScanOutputValidatorTests`
Expected: FAIL (class doesn't exist).

- [ ] **Step 3: Implement the validator**

```csharp
using System.Text.Json;

namespace PatchHound.Infrastructure.AuthenticatedScans;

public record ValidatedSoftwareEntry(
    string CanonicalName,
    string CanonicalProductKey,
    string? CanonicalVendor,
    string? Category,
    string? PrimaryCpe23Uri,
    string? DetectedVersion);

public record ValidationIssueRecord(int EntryIndex, string FieldPath, string Message);

public record OutputValidationResult(
    bool FatalError,
    string FatalErrorMessage,
    List<ValidatedSoftwareEntry> ValidEntries,
    List<ValidationIssueRecord> Issues);

public class AuthenticatedScanOutputValidator
{
    private const int MaxEntries = 5000;
    private const int MaxStringLength = 1024;

    public OutputValidationResult Validate(string json)
    {
        var valid = new List<ValidatedSoftwareEntry>();
        var issues = new List<ValidationIssueRecord>();

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { return Fatal($"invalid JSON: {ex.Message}"); }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return Fatal("root must be an object");
            if (!doc.RootElement.TryGetProperty("software", out var arr))
                return Fatal("missing 'software' property");
            if (arr.ValueKind != JsonValueKind.Array)
                return Fatal("'software' must be an array");
            var count = arr.GetArrayLength();
            if (count > MaxEntries)
                return Fatal($"entry count {count} exceeds limit of {MaxEntries}");

            var index = 0;
            foreach (var element in arr.EnumerateArray())
            {
                var entryValid = TryValidateEntry(element, index, issues, out var entry);
                if (entryValid) valid.Add(entry!);
                index++;
            }
        }

        return new OutputValidationResult(false, string.Empty, valid, issues);
    }

    private static OutputValidationResult Fatal(string msg) =>
        new(true, msg, new(), new());

    private static bool TryValidateEntry(JsonElement el, int index, List<ValidationIssueRecord> issues, out ValidatedSoftwareEntry? entry)
    {
        entry = null;
        if (el.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new(index, "$", "entry must be an object"));
            return false;
        }

        var name = ReadRequiredString(el, "canonicalName", index, issues);
        var key = ReadRequiredString(el, "canonicalProductKey", index, issues);
        var vendor = ReadOptionalString(el, "canonicalVendor", index, issues);
        var category = ReadOptionalString(el, "category", index, issues);
        var cpe = ReadOptionalString(el, "primaryCpe23Uri", index, issues);
        var version = ReadOptionalString(el, "detectedVersion", index, issues);

        if (name is null || key is null) return false;
        entry = new ValidatedSoftwareEntry(name, key, vendor, category, cpe, version);
        return true;
    }

    private static string? ReadRequiredString(JsonElement el, string prop, int index, List<ValidationIssueRecord> issues)
    {
        if (!el.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            issues.Add(new(index, $"$.software[{index}].{prop}", $"missing required field '{prop}'"));
            return null;
        }
        if (v.ValueKind != JsonValueKind.String)
        {
            issues.Add(new(index, $"$.software[{index}].{prop}", $"'{prop}' must be a string"));
            return null;
        }
        var s = v.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(s))
        {
            issues.Add(new(index, $"$.software[{index}].{prop}", $"'{prop}' cannot be empty"));
            return null;
        }
        if (s.Length > MaxStringLength)
        {
            issues.Add(new(index, $"$.software[{index}].{prop}", $"'{prop}' exceeds {MaxStringLength} chars"));
            return null;
        }
        return s;
    }

    private static string? ReadOptionalString(JsonElement el, string prop, int index, List<ValidationIssueRecord> issues)
    {
        if (!el.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.String)
        {
            issues.Add(new(index, $"$.software[{index}].{prop}", $"'{prop}' must be a string"));
            return null;
        }
        var s = v.GetString() ?? "";
        if (s.Length > MaxStringLength)
        {
            issues.Add(new(index, $"$.software[{index}].{prop}", $"'{prop}' exceeds {MaxStringLength} chars"));
            return null;
        }
        return s;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~AuthenticatedScanOutputValidatorTests`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/AuthenticatedScans/AuthenticatedScanOutputValidator.cs tests/PatchHound.Tests/Infrastructure/AuthenticatedScans/AuthenticatedScanOutputValidatorTests.cs
git commit -m "feat: add scan output validator with tests"
```

---

## Task 11: `AuthenticatedScanIngestionService` — staging + merge

**Files:**
- Create: `src/PatchHound.Infrastructure/AuthenticatedScans/AuthenticatedScanIngestionService.cs`
- Create: `tests/PatchHound.Tests/Infrastructure/AuthenticatedScans/AuthenticatedScanIngestionServiceTests.cs`

Validates a job's parsed JSON, writes staging rows, merges into `NormalizedSoftware` + `NormalizedSoftwareInstallation` with per-source deactivation, updates the job's final status.

Read §7 of the spec before writing this service.

- [ ] **Step 1: Examine how Defender's staging → merge logic upserts `NormalizedSoftware` today**

Run: `grep -r "NormalizedSoftware" src/PatchHound.Infrastructure --include="*.cs" -l`
Open the hits, locate the logic that creates/updates `NormalizedSoftware` + `NormalizedSoftwareInstallation` + `DeviceSoftwareInstallation` + `TenantSoftware` rows. The new service must upsert these rows the same way, but scoped to a single `(tenantId, deviceAssetId, SourceSystem=AuthenticatedScan)`. If the existing logic lives in a shared helper, **reuse it**. If it's private to the Defender pipeline, extract it to a small shared helper class (e.g. `NormalizedSoftwareMergeHelper`) in `src/PatchHound.Infrastructure/AuthenticatedScans/` that both pipelines call. Record the decision in this task's commit message.

- [ ] **Step 2: Write failing integration test (happy path, Defender coexistence)**

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.AuthenticatedScans;
using PatchHound.Infrastructure.Data;
using Xunit;

namespace PatchHound.Tests.Infrastructure.AuthenticatedScans;

public class AuthenticatedScanIngestionServiceTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private AuthenticatedScanIngestionService _sut = null!;
    private Guid _tenantId = Guid.NewGuid();
    private Guid _deviceAssetId;
    private Guid _scanProfileId = Guid.NewGuid();
    private Guid _scanJobId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _db = TestDbContextFactory.CreateInMemory(); // use existing shared factory
        // seed: tenant, device asset
        _deviceAssetId = await TestData.SeedDevice(_db, _tenantId, "host-1");
        var job = ScanJob.Create(_tenantId, Guid.NewGuid(), Guid.NewGuid(), _deviceAssetId, Guid.NewGuid(), "[]");
        // reflection or internal setter to set Id = _scanJobId, or use returned Id
        _scanJobId = job.Id;
        _db.ScanJobs.Add(job);
        _db.ScanJobResults.Add(ScanJobResult.Create(job.Id, "stdout", "", "{}"));
        await _db.SaveChangesAsync();
        _sut = new AuthenticatedScanIngestionService(_db, new AuthenticatedScanOutputValidator());
    }

    public Task DisposeAsync() { _db.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Ingest_creates_normalized_software_installation_for_valid_entry()
    {
        var json = """{"software":[{"canonicalName":"nginx","canonicalProductKey":"nginx:nginx","detectedVersion":"1.24.0"}]}""";
        await UpdateJobResult(json);

        var summary = await _sut.IngestAsync(_scanJobId, _scanProfileId, CancellationToken.None);

        Assert.Equal(1, summary.EntriesIngested);
        var installation = await _db.NormalizedSoftwareInstallations
            .SingleAsync(i => i.DeviceAssetId == _deviceAssetId && i.SourceSystem == SoftwareIdentitySourceSystem.AuthenticatedScan);
        Assert.Equal("1.24.0", installation.DetectedVersion);
        Assert.True(installation.IsActive);
    }

    [Fact]
    public async Task Ingest_deactivates_previously_reported_software_not_in_new_run()
    {
        // first run: nginx present
        await UpdateJobResult("""{"software":[{"canonicalName":"nginx","canonicalProductKey":"nginx:nginx"}]}""");
        await _sut.IngestAsync(_scanJobId, _scanProfileId, CancellationToken.None);

        // second run: nginx absent, httpd present
        var job2 = ScanJob.Create(_tenantId, Guid.NewGuid(), Guid.NewGuid(), _deviceAssetId, Guid.NewGuid(), "[]");
        _db.ScanJobs.Add(job2);
        _db.ScanJobResults.Add(ScanJobResult.Create(job2.Id, "", "",
            """{"software":[{"canonicalName":"httpd","canonicalProductKey":"apache:httpd"}]}"""));
        await _db.SaveChangesAsync();

        await _sut.IngestAsync(job2.Id, _scanProfileId, CancellationToken.None);

        var nginx = await _db.NormalizedSoftwareInstallations
            .SingleAsync(i => i.DeviceAssetId == _deviceAssetId
                && i.SourceSystem == SoftwareIdentitySourceSystem.AuthenticatedScan
                && EF.Property<string>(i, "CanonicalProductKey") == "nginx:nginx");
        Assert.False(nginx.IsActive);
        var httpd = await _db.NormalizedSoftwareInstallations
            .SingleAsync(i => i.DeviceAssetId == _deviceAssetId && i.SourceSystem == SoftwareIdentitySourceSystem.AuthenticatedScan
                && EF.Property<string>(i, "CanonicalProductKey") == "apache:httpd");
        Assert.True(httpd.IsActive);
    }

    [Fact]
    public async Task Ingest_does_not_touch_defender_source_rows()
    {
        // pre-seed a Defender row for the same device+software
        await TestData.SeedDefenderInstallation(_db, _tenantId, _deviceAssetId, "nginx:nginx", "1.20.0");

        await UpdateJobResult("""{"software":[{"canonicalName":"nginx","canonicalProductKey":"nginx:nginx","detectedVersion":"1.24.0"}]}""");
        await _sut.IngestAsync(_scanJobId, _scanProfileId, CancellationToken.None);

        var defender = await _db.NormalizedSoftwareInstallations
            .SingleAsync(i => i.DeviceAssetId == _deviceAssetId && i.SourceSystem == SoftwareIdentitySourceSystem.Defender);
        Assert.Equal("1.20.0", defender.DetectedVersion);
        Assert.True(defender.IsActive);

        var auth = await _db.NormalizedSoftwareInstallations
            .SingleAsync(i => i.DeviceAssetId == _deviceAssetId && i.SourceSystem == SoftwareIdentitySourceSystem.AuthenticatedScan);
        Assert.Equal("1.24.0", auth.DetectedVersion);
    }

    [Fact]
    public async Task Ingest_records_validation_issues_and_ingests_valid_entries()
    {
        await UpdateJobResult("""
            {"software":[
              {"canonicalName":"nginx","canonicalProductKey":"nginx:nginx"},
              {"canonicalName":""}
            ]}""");
        var summary = await _sut.IngestAsync(_scanJobId, _scanProfileId, CancellationToken.None);
        Assert.Equal(1, summary.EntriesIngested);
        var issues = await _db.ScanJobValidationIssues.Where(i => i.ScanJobId == _scanJobId).ToListAsync();
        Assert.Single(issues);
    }

    [Fact]
    public async Task Ingest_fails_job_when_zero_valid_entries()
    {
        await UpdateJobResult("""{"software":[{"canonicalName":""}]}""");
        var summary = await _sut.IngestAsync(_scanJobId, _scanProfileId, CancellationToken.None);
        Assert.Equal(0, summary.EntriesIngested);
        Assert.True(summary.JobFailed);
        var job = await _db.ScanJobs.SingleAsync(j => j.Id == _scanJobId);
        Assert.Equal(ScanJobStatuses.Failed, job.Status);
    }

    private async Task UpdateJobResult(string json)
    {
        var r = await _db.ScanJobResults.SingleAsync(x => x.ScanJobId == _scanJobId);
        // update ParsedJson via reflection or re-create — depends on entity model; simplest: remove + add
        _db.ScanJobResults.Remove(r);
        _db.ScanJobResults.Add(ScanJobResult.Create(_scanJobId, "", "", json));
        await _db.SaveChangesAsync();
    }
}
```

Note: this uses a `TestDbContextFactory.CreateInMemory()` and `TestData.SeedDevice` / `TestData.SeedDefenderInstallation` helpers — check `tests/PatchHound.Tests/TestData/` for existing equivalents and reuse them; if they don't exist for these cases, add them to the shared TestData module.

- [ ] **Step 3: Run tests — expected to fail (service doesn't exist)**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~AuthenticatedScanIngestionServiceTests`
Expected: FAIL.

- [ ] **Step 4: Implement the service**

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.AuthenticatedScans;

public record IngestionSummary(int EntriesIngested, bool JobFailed, string? ErrorMessage);

public class AuthenticatedScanIngestionService
{
    private readonly PatchHoundDbContext _db;
    private readonly AuthenticatedScanOutputValidator _validator;

    public AuthenticatedScanIngestionService(PatchHoundDbContext db, AuthenticatedScanOutputValidator validator)
    {
        _db = db;
        _validator = validator;
    }

    public async Task<IngestionSummary> IngestAsync(Guid scanJobId, Guid scanProfileId, CancellationToken ct)
    {
        var job = await _db.ScanJobs.SingleAsync(j => j.Id == scanJobId, ct);
        var result = await _db.ScanJobResults.SingleAsync(r => r.ScanJobId == scanJobId, ct);

        var validation = _validator.Validate(result.ParsedJson);
        if (validation.FatalError)
        {
            job.CompleteFailed(ScanJobStatuses.Failed, $"output validation failed: {validation.FatalErrorMessage}", DateTimeOffset.UtcNow);
            await _db.SaveChangesAsync(ct);
            return new IngestionSummary(0, true, validation.FatalErrorMessage);
        }

        foreach (var issue in validation.Issues)
        {
            _db.ScanJobValidationIssues.Add(
                ScanJobValidationIssue.Create(scanJobId, issue.FieldPath, issue.Message, issue.EntryIndex));
        }

        if (validation.ValidEntries.Count == 0)
        {
            job.CompleteFailed(ScanJobStatuses.Failed, $"all {validation.Issues.Count} entries failed validation", DateTimeOffset.UtcNow);
            await _db.SaveChangesAsync(ct);
            return new IngestionSummary(0, true, "all entries invalid");
        }

        // Stage
        foreach (var entry in validation.ValidEntries)
        {
            _db.StagedAuthenticatedScanSoftware.Add(
                StagedAuthenticatedScanSoftware.Create(
                    job.TenantId, scanJobId, job.AssetId, scanProfileId,
                    entry.CanonicalName, entry.CanonicalProductKey,
                    entry.CanonicalVendor, entry.Category, entry.PrimaryCpe23Uri, entry.DetectedVersion));
        }

        // Merge
        await MergeAsync(job.TenantId, job.AssetId, validation.ValidEntries, ct);

        job.CompleteSucceeded(
            stdoutBytes: result.RawStdout.Length,
            stderrBytes: result.RawStderr.Length,
            entriesIngested: validation.ValidEntries.Count,
            at: DateTimeOffset.UtcNow);

        await _db.SaveChangesAsync(ct);
        return new IngestionSummary(validation.ValidEntries.Count, false, null);
    }

    private async Task MergeAsync(Guid tenantId, Guid deviceAssetId, List<ValidatedSoftwareEntry> entries, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var e in entries)
        {
            seenKeys.Add(e.CanonicalProductKey);

            // 1. Upsert NormalizedSoftware by CanonicalProductKey
            var normalized = await _db.NormalizedSoftware
                .SingleOrDefaultAsync(n => n.CanonicalProductKey == e.CanonicalProductKey, ct);
            if (normalized is null)
            {
                normalized = NormalizedSoftware.Create(
                    e.CanonicalName, e.CanonicalVendor, e.Category, e.CanonicalProductKey, e.PrimaryCpe23Uri,
                    SoftwareNormalizationMethod.ExternalSource, SoftwareNormalizationConfidence.High, now);
                _db.NormalizedSoftware.Add(normalized);
                await _db.SaveChangesAsync(ct);
            }

            // 2. Ensure TenantSoftware + software Asset exist.
            //    Reuse existing helper (see Step 1). If not extracted, add a helper that
            //    returns (tenantSoftwareId, softwareAssetId) for a given (tenantId, normalizedSoftwareId).
            var (tenantSoftwareId, softwareAssetId) = await EnsureTenantSoftwareAsync(tenantId, normalized.Id, ct);

            // 3. Upsert NormalizedSoftwareInstallation by (tenantId, deviceAssetId, tenantSoftwareId, SourceSystem)
            var installation = await _db.NormalizedSoftwareInstallations
                .SingleOrDefaultAsync(i => i.TenantId == tenantId
                    && i.DeviceAssetId == deviceAssetId
                    && i.TenantSoftwareId == tenantSoftwareId
                    && i.SourceSystem == SoftwareIdentitySourceSystem.AuthenticatedScan, ct);

            if (installation is null)
            {
                installation = NormalizedSoftwareInstallation.Create(
                    tenantId, snapshotId: null, tenantSoftwareId, softwareAssetId, deviceAssetId,
                    SoftwareIdentitySourceSystem.AuthenticatedScan, e.DetectedVersion,
                    firstSeenAt: now, lastSeenAt: now, removedAt: null, isActive: true, currentEpisodeNumber: 1);
                _db.NormalizedSoftwareInstallations.Add(installation);
            }
            else
            {
                installation.UpdateProjection(
                    snapshotId: null, tenantSoftwareId, SoftwareIdentitySourceSystem.AuthenticatedScan,
                    e.DetectedVersion,
                    firstSeenAt: installation.FirstSeenAt, lastSeenAt: now, removedAt: null,
                    isActive: true, currentEpisodeNumber: installation.CurrentEpisodeNumber);
            }

            // 4. Touch DeviceSoftwareInstallation
            var dsi = await _db.DeviceSoftwareInstallations.SingleOrDefaultAsync(
                d => d.TenantId == tenantId && d.DeviceAssetId == deviceAssetId && d.SoftwareAssetId == softwareAssetId, ct);
            if (dsi is null)
                _db.DeviceSoftwareInstallations.Add(DeviceSoftwareInstallation.Create(tenantId, deviceAssetId, softwareAssetId, now));
            else
                dsi.Touch(now);
        }

        // 5. Deactivate prior auth-scan rows for this device that are not in the current set.
        var existing = await _db.NormalizedSoftwareInstallations
            .Where(i => i.TenantId == tenantId
                && i.DeviceAssetId == deviceAssetId
                && i.SourceSystem == SoftwareIdentitySourceSystem.AuthenticatedScan
                && i.IsActive)
            .Join(_db.NormalizedSoftware, i => i.TenantSoftwareId /*via TenantSoftware->NormalizedSoftware*/, n => n.Id, (i, n) => new { i, n.CanonicalProductKey })
            .ToListAsync(ct);

        foreach (var row in existing.Where(r => !seenKeys.Contains(r.CanonicalProductKey)))
        {
            row.i.UpdateProjection(
                snapshotId: null, row.i.TenantSoftwareId, SoftwareIdentitySourceSystem.AuthenticatedScan,
                row.i.DetectedVersion,
                firstSeenAt: row.i.FirstSeenAt, lastSeenAt: row.i.LastSeenAt, removedAt: now,
                isActive: false, currentEpisodeNumber: row.i.CurrentEpisodeNumber);
        }
    }

    private async Task<(Guid tenantSoftwareId, Guid softwareAssetId)> EnsureTenantSoftwareAsync(
        Guid tenantId, Guid normalizedSoftwareId, CancellationToken ct)
    {
        // TODO (during implementation): replace this placeholder with a call into the existing
        // helper identified in Task 11 Step 1. If extracted, call NormalizedSoftwareMergeHelper.EnsureTenantSoftwareAsync.
        // If the existing Defender pipeline has this behavior inline, lift it into such a helper first.
        throw new NotImplementedException(
            "Implement using existing tenant-software ensure logic from the Defender ingestion pipeline. "
            + "See Task 11 Step 1 for the refactor direction.");
    }
}
```

Note: the join in step 5 uses `TenantSoftwareId` as if it equals `NormalizedSoftware.Id` for notational convenience. In reality you must navigate through `TenantSoftware` → `NormalizedSoftwareId`. Adjust the query to join `NormalizedSoftwareInstallation.TenantSoftwareId` → `TenantSoftware.Id`, then `TenantSoftware.NormalizedSoftwareId` → `NormalizedSoftware.Id`.

- [ ] **Step 5: Register service in DI**

In `src/PatchHound.Infrastructure/DependencyInjection.cs`:

```csharp
services.AddScoped<AuthenticatedScanOutputValidator>();
services.AddScoped<AuthenticatedScanIngestionService>();
```

- [ ] **Step 6: Run tests until green**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~AuthenticatedScanIngestionServiceTests`
Expected: all pass. Fix join/ensure-tenant-software logic until green.

- [ ] **Step 7: Commit**

```bash
git add src/PatchHound.Infrastructure/AuthenticatedScans/AuthenticatedScanIngestionService.cs src/PatchHound.Infrastructure/DependencyInjection.cs tests/PatchHound.Tests/Infrastructure/AuthenticatedScans/AuthenticatedScanIngestionServiceTests.cs
git commit -m "feat: add AuthenticatedScanIngestionService with staging+merge and per-source deactivation"
```

---

## Task 12: `ScanJobDispatcher` with tests (TDD)

**Files:**
- Create: `src/PatchHound.Infrastructure/AuthenticatedScans/ScanJobDispatcher.cs`
- Create: `tests/PatchHound.Tests/Infrastructure/AuthenticatedScans/ScanJobDispatcherTests.cs`

Per §6.2 of the spec: creates a run, enumerates assigned assets, snapshots tool version ids, creates `ScanJob` per asset. Rejects when profile has an active non-terminal run.

- [ ] **Step 1: Write failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Infrastructure.AuthenticatedScans;
using PatchHound.Infrastructure.Data;
using Xunit;

namespace PatchHound.Tests.Infrastructure.AuthenticatedScans;

public class ScanJobDispatcherTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private ScanJobDispatcher _sut = null!;
    private Guid _tenantId = Guid.NewGuid();
    private ScanProfile _profile = null!;

    public async Task InitializeAsync()
    {
        _db = TestDbContextFactory.CreateInMemory();
        var runnerId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        _db.ScanRunners.Add(ScanRunner.Create(_tenantId, "r1", "", "hash"));
        _db.ConnectionProfiles.Add(ConnectionProfile.Create(_tenantId, "c1", "", "h", 22, "u", "password", "path", null));
        _profile = ScanProfile.Create(_tenantId, "p1", "", "", connId, runnerId, true);
        _db.ScanProfiles.Add(_profile);

        // one tool with a current version
        var tool = ScanningTool.Create(_tenantId, "t1", "", "python", "/usr/bin/python3", 300, "NormalizedSoftware");
        var v = ScanningToolVersion.Create(tool.Id, 1, "print('hi')", Guid.NewGuid());
        tool.SetCurrentVersion(v.Id);
        _db.ScanningTools.Add(tool);
        _db.ScanningToolVersions.Add(v);
        _db.ScanProfileTools.Add(ScanProfileTool.Create(_profile.Id, tool.Id, 0));
        await _db.SaveChangesAsync();

        _sut = new ScanJobDispatcher(_db);
    }

    public Task DisposeAsync() { _db.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task StartRun_creates_one_job_per_assigned_asset()
    {
        var a1 = await TestData.SeedDevice(_db, _tenantId, "h1");
        var a2 = await TestData.SeedDevice(_db, _tenantId, "h2");
        _db.AssetScanProfileAssignments.Add(AssetScanProfileAssignment.Create(_tenantId, a1, _profile.Id, null));
        _db.AssetScanProfileAssignments.Add(AssetScanProfileAssignment.Create(_tenantId, a2, _profile.Id, null));
        await _db.SaveChangesAsync();

        var runId = await _sut.StartRunAsync(_profile.Id, "scheduled", null, CancellationToken.None);

        var run = await _db.AuthenticatedScanRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(2, run.TotalDevices);
        Assert.Equal(AuthenticatedScanRunStatuses.Running, run.Status);
        Assert.Equal(2, await _db.ScanJobs.CountAsync(j => j.RunId == runId));
    }

    [Fact]
    public async Task StartRun_with_zero_assets_marks_run_succeeded_immediately()
    {
        var runId = await _sut.StartRunAsync(_profile.Id, "scheduled", null, CancellationToken.None);
        var run = await _db.AuthenticatedScanRuns.SingleAsync(r => r.Id == runId);
        Assert.Equal(AuthenticatedScanRunStatuses.Succeeded, run.Status);
        Assert.Equal(0, run.TotalDevices);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public async Task StartRun_throws_when_profile_has_active_run()
    {
        var a1 = await TestData.SeedDevice(_db, _tenantId, "h1");
        _db.AssetScanProfileAssignments.Add(AssetScanProfileAssignment.Create(_tenantId, a1, _profile.Id, null));
        await _db.SaveChangesAsync();

        await _sut.StartRunAsync(_profile.Id, "scheduled", null, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StartRunAsync(_profile.Id, "manual", Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task StartRun_snapshots_current_tool_version_ids_into_job()
    {
        var a1 = await TestData.SeedDevice(_db, _tenantId, "h1");
        _db.AssetScanProfileAssignments.Add(AssetScanProfileAssignment.Create(_tenantId, a1, _profile.Id, null));
        await _db.SaveChangesAsync();

        var runId = await _sut.StartRunAsync(_profile.Id, "scheduled", null, CancellationToken.None);
        var job = await _db.ScanJobs.SingleAsync(j => j.RunId == runId);
        Assert.Contains("\"", job.ScanningToolVersionIdsJson);
        Assert.NotEqual("[]", job.ScanningToolVersionIdsJson);
    }
}
```

- [ ] **Step 2: Run tests — expected to fail**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~ScanJobDispatcherTests`
Expected: FAIL (class doesn't exist).

- [ ] **Step 3: Implement the dispatcher**

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.AuthenticatedScans;

public class ScanJobDispatcher
{
    private readonly PatchHoundDbContext _db;
    public ScanJobDispatcher(PatchHoundDbContext db) { _db = db; }

    public async Task<Guid> StartRunAsync(Guid scanProfileId, string triggerKind, Guid? triggeredByUserId, CancellationToken ct)
    {
        var profile = await _db.ScanProfiles.SingleAsync(p => p.Id == scanProfileId, ct);

        // Guard: no active run for this profile
        var active = await _db.AuthenticatedScanRuns.AnyAsync(r => r.ScanProfileId == scanProfileId
            && r.Status != AuthenticatedScanRunStatuses.Succeeded
            && r.Status != AuthenticatedScanRunStatuses.Failed
            && r.Status != AuthenticatedScanRunStatuses.PartiallyFailed, ct);
        if (active)
            throw new InvalidOperationException($"scan profile {scanProfileId} already has an active run");

        var now = DateTimeOffset.UtcNow;
        var run = AuthenticatedScanRun.Start(profile.TenantId, scanProfileId, triggerKind, triggeredByUserId, now);
        _db.AuthenticatedScanRuns.Add(run);

        // Snapshot tool version ids
        var toolIds = await _db.ScanProfileTools
            .Where(t => t.ScanProfileId == scanProfileId)
            .OrderBy(t => t.ExecutionOrder)
            .Select(t => t.ScanningToolId).ToListAsync(ct);
        var versionIds = await _db.ScanningTools
            .Where(t => toolIds.Contains(t.Id) && t.CurrentVersionId != null)
            .Select(t => t.CurrentVersionId!.Value)
            .ToListAsync(ct);
        var versionIdsJson = JsonSerializer.Serialize(versionIds);

        // Assigned assets
        var assetIds = await _db.AssetScanProfileAssignments
            .Where(a => a.ScanProfileId == scanProfileId)
            .Select(a => a.AssetId).ToListAsync(ct);

        if (assetIds.Count == 0)
        {
            run.MarkRunning(0);
            run.Complete(0, 0, 0, now);
            await _db.SaveChangesAsync(ct);
            return run.Id;
        }

        foreach (var assetId in assetIds)
        {
            _db.ScanJobs.Add(ScanJob.Create(
                profile.TenantId, run.Id, profile.ScanRunnerId, assetId, profile.ConnectionProfileId, versionIdsJson));
        }
        run.MarkRunning(assetIds.Count);
        profile.RecordRunStarted(now);
        profile.ClearManualRequest();

        await _db.SaveChangesAsync(ct);
        return run.Id;
    }
}
```

- [ ] **Step 4: Register in DI + run tests**

Add `services.AddScoped<ScanJobDispatcher>();` in `DependencyInjection.cs`.

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~ScanJobDispatcherTests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/AuthenticatedScans/ScanJobDispatcher.cs src/PatchHound.Infrastructure/DependencyInjection.cs tests/PatchHound.Tests/Infrastructure/AuthenticatedScans/ScanJobDispatcherTests.cs
git commit -m "feat: add ScanJobDispatcher with concurrency guardrail and version snapshot"
```

---

## Task 13: Scanning tool version retention (10-version rolling window) with tests

**Files:**
- Create: `src/PatchHound.Infrastructure/AuthenticatedScans/ScanningToolVersionStore.cs`
- Create: `tests/PatchHound.Tests/Infrastructure/AuthenticatedScans/ScanningToolVersionRetentionTests.cs`

When an admin saves a new version of a scanning tool's script, store it and prune oldest so only 10 remain per tool.

- [ ] **Step 1: Write failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Infrastructure.AuthenticatedScans;
using PatchHound.Infrastructure.Data;
using Xunit;

namespace PatchHound.Tests.Infrastructure.AuthenticatedScans;

public class ScanningToolVersionRetentionTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private ScanningToolVersionStore _sut = null!;
    private ScanningTool _tool = null!;
    private Guid _userId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _db = TestDbContextFactory.CreateInMemory();
        _tool = ScanningTool.Create(Guid.NewGuid(), "t", "", "bash", "/bin/bash", 60, "NormalizedSoftware");
        _db.ScanningTools.Add(_tool);
        await _db.SaveChangesAsync();
        _sut = new ScanningToolVersionStore(_db);
    }
    public Task DisposeAsync() { _db.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task AppendVersion_assigns_incrementing_version_numbers_starting_at_1()
    {
        var v1 = await _sut.AppendAsync(_tool.Id, "echo 1", _userId, CancellationToken.None);
        var v2 = await _sut.AppendAsync(_tool.Id, "echo 2", _userId, CancellationToken.None);
        Assert.Equal(1, v1.VersionNumber);
        Assert.Equal(2, v2.VersionNumber);
    }

    [Fact]
    public async Task AppendVersion_sets_tool_CurrentVersionId_to_new_version()
    {
        var v = await _sut.AppendAsync(_tool.Id, "echo 1", _userId, CancellationToken.None);
        var refreshed = await _db.ScanningTools.SingleAsync(t => t.Id == _tool.Id);
        Assert.Equal(v.Id, refreshed.CurrentVersionId);
    }

    [Fact]
    public async Task AppendVersion_prunes_oldest_keeping_newest_10()
    {
        for (var i = 1; i <= 12; i++)
            await _sut.AppendAsync(_tool.Id, $"echo {i}", _userId, CancellationToken.None);
        var remaining = await _db.ScanningToolVersions
            .Where(v => v.ScanningToolId == _tool.Id)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync();
        Assert.Equal(10, remaining.Count);
        Assert.Equal(3, remaining.First().VersionNumber);
        Assert.Equal(12, remaining.Last().VersionNumber);
    }
}
```

- [ ] **Step 2: Run tests — expected to fail**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~ScanningToolVersionRetentionTests`
Expected: FAIL.

- [ ] **Step 3: Implement the store**

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Infrastructure.AuthenticatedScans;

public class ScanningToolVersionStore
{
    private const int RetainCount = 10;
    private readonly PatchHoundDbContext _db;
    public ScanningToolVersionStore(PatchHoundDbContext db) { _db = db; }

    public async Task<ScanningToolVersion> AppendAsync(Guid scanningToolId, string scriptContent, Guid editedByUserId, CancellationToken ct)
    {
        var tool = await _db.ScanningTools.SingleAsync(t => t.Id == scanningToolId, ct);
        var maxVersion = await _db.ScanningToolVersions
            .Where(v => v.ScanningToolId == scanningToolId)
            .Select(v => (int?)v.VersionNumber).MaxAsync(ct) ?? 0;
        var next = ScanningToolVersion.Create(scanningToolId, maxVersion + 1, scriptContent, editedByUserId);
        _db.ScanningToolVersions.Add(next);
        tool.SetCurrentVersion(next.Id);

        // Prune oldest beyond RetainCount
        var stale = await _db.ScanningToolVersions
            .Where(v => v.ScanningToolId == scanningToolId)
            .OrderByDescending(v => v.VersionNumber)
            .Skip(RetainCount - 1) // -1 because the new version will be added at SaveChanges
            .ToListAsync(ct);
        _db.ScanningToolVersions.RemoveRange(stale);
        await _db.SaveChangesAsync(ct);
        return next;
    }
}
```

- [ ] **Step 4: Register in DI + run tests**

Add `services.AddScoped<ScanningToolVersionStore>();` to `DependencyInjection.cs`.

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~ScanningToolVersionRetentionTests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/PatchHound.Infrastructure/AuthenticatedScans/ScanningToolVersionStore.cs src/PatchHound.Infrastructure/DependencyInjection.cs tests/PatchHound.Tests/Infrastructure/AuthenticatedScans/ScanningToolVersionRetentionTests.cs
git commit -m "feat: add ScanningToolVersionStore with 10-version rolling window"
```

---

## Task 14: `ConnectionProfilesController` with DTOs and tests

**Files:**
- Create: `src/PatchHound.Api/Models/AuthenticatedScans/ConnectionProfileDtos.cs`
- Create: `src/PatchHound.Api/Controllers/ConnectionProfilesController.cs`
- Create: `tests/PatchHound.Tests/Api/ConnectionProfilesControllerTests.cs`

CRUD endpoints guarded by `Policies.ConfigureTenant` (same as existing admin controllers).

- [ ] **Step 1: DTOs**

```csharp
namespace PatchHound.Api.Models.AuthenticatedScans;

public record ConnectionProfileDto(
    Guid Id, string Name, string Description, string Kind,
    string SshHost, int SshPort, string SshUsername, string AuthMethod,
    string? HostKeyFingerprint, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public record CreateConnectionProfileRequest(
    string Name, string Description, string SshHost, int SshPort, string SshUsername,
    string AuthMethod,
    string? Password, string? PrivateKey, string? PrivateKeyPassphrase,
    string? HostKeyFingerprint);

public record UpdateConnectionProfileRequest(
    string Name, string Description, string SshHost, int SshPort, string SshUsername,
    string AuthMethod, string? HostKeyFingerprint);

public record ReplaceConnectionProfileSecretRequest(
    string AuthMethod, string? Password, string? PrivateKey, string? PrivateKeyPassphrase);
```

- [ ] **Step 2: Controller**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.AuthenticatedScans;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.AuthenticatedScans;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/authenticated-scans/connection-profiles")]
[Authorize(Policy = Policies.ConfigureTenant)]
public class ConnectionProfilesController : ControllerBase
{
    private readonly PatchHoundDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ConnectionProfileSecretWriter _secrets;
    private readonly AuditLogWriter _audit;

    public ConnectionProfilesController(PatchHoundDbContext db, ITenantContext tenant,
        ConnectionProfileSecretWriter secrets, AuditLogWriter audit)
    { _db = db; _tenant = tenant; _secrets = secrets; _audit = audit; }

    [HttpGet]
    public async Task<IEnumerable<ConnectionProfileDto>> List(CancellationToken ct)
    {
        var items = await _db.ConnectionProfiles.AsNoTracking()
            .Where(c => c.TenantId == _tenant.TenantId)
            .OrderBy(c => c.Name).ToListAsync(ct);
        return items.Select(ToDto);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ConnectionProfileDto>> Get(Guid id, CancellationToken ct)
    {
        var item = await _db.ConnectionProfiles.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id && c.TenantId == _tenant.TenantId, ct);
        return item is null ? NotFound() : ToDto(item);
    }

    [HttpPost]
    public async Task<ActionResult<ConnectionProfileDto>> Create([FromBody] CreateConnectionProfileRequest req, CancellationToken ct)
    {
        var profileId = Guid.NewGuid();
        string secretRef;
        if (req.AuthMethod == "password")
        {
            if (string.IsNullOrEmpty(req.Password)) return BadRequest("password required");
            secretRef = await _secrets.WritePasswordAsync(_tenant.TenantId, profileId, req.Password, ct);
        }
        else if (req.AuthMethod == "privateKey")
        {
            if (string.IsNullOrEmpty(req.PrivateKey)) return BadRequest("privateKey required");
            secretRef = await _secrets.WritePrivateKeyAsync(_tenant.TenantId, profileId, req.PrivateKey, req.PrivateKeyPassphrase, ct);
        }
        else return BadRequest("authMethod must be password|privateKey");

        var profile = ConnectionProfile.Create(
            _tenant.TenantId, req.Name, req.Description, req.SshHost, req.SshPort,
            req.SshUsername, req.AuthMethod, secretRef, req.HostKeyFingerprint);
        // force id to match secret path
        typeof(ConnectionProfile).GetProperty(nameof(ConnectionProfile.Id))!.SetValue(profile, profileId);
        _db.ConnectionProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("ConnectionProfile.Created", profile.Id.ToString(), ct);
        return CreatedAtAction(nameof(Get), new { id = profile.Id }, ToDto(profile));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ConnectionProfileDto>> Update(Guid id, [FromBody] UpdateConnectionProfileRequest req, CancellationToken ct)
    {
        var p = await _db.ConnectionProfiles.SingleOrDefaultAsync(c => c.Id == id && c.TenantId == _tenant.TenantId, ct);
        if (p is null) return NotFound();
        p.Update(req.Name, req.Description, req.SshHost, req.SshPort, req.SshUsername, req.AuthMethod, req.HostKeyFingerprint);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("ConnectionProfile.Updated", id.ToString(), ct);
        return ToDto(p);
    }

    [HttpPost("{id:guid}/replace-secret")]
    public async Task<IActionResult> ReplaceSecret(Guid id, [FromBody] ReplaceConnectionProfileSecretRequest req, CancellationToken ct)
    {
        var p = await _db.ConnectionProfiles.SingleOrDefaultAsync(c => c.Id == id && c.TenantId == _tenant.TenantId, ct);
        if (p is null) return NotFound();
        string secretRef;
        if (req.AuthMethod == "password")
        {
            if (string.IsNullOrEmpty(req.Password)) return BadRequest("password required");
            secretRef = await _secrets.WritePasswordAsync(_tenant.TenantId, id, req.Password, ct);
        }
        else if (req.AuthMethod == "privateKey")
        {
            if (string.IsNullOrEmpty(req.PrivateKey)) return BadRequest("privateKey required");
            secretRef = await _secrets.WritePrivateKeyAsync(_tenant.TenantId, id, req.PrivateKey, req.PrivateKeyPassphrase, ct);
        }
        else return BadRequest("authMethod must be password|privateKey");
        p.SetSecretRef(secretRef);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("ConnectionProfile.SecretReplaced", id.ToString(), ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var p = await _db.ConnectionProfiles.SingleOrDefaultAsync(c => c.Id == id && c.TenantId == _tenant.TenantId, ct);
        if (p is null) return NotFound();
        // guard: reject if referenced by a ScanProfile
        var inUse = await _db.ScanProfiles.AnyAsync(sp => sp.ConnectionProfileId == id, ct);
        if (inUse) return Conflict("connection profile is referenced by a scan profile");
        _db.ConnectionProfiles.Remove(p);
        await _secrets.DeleteAsync(_tenant.TenantId, id, ct);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("ConnectionProfile.Deleted", id.ToString(), ct);
        return NoContent();
    }

    private static ConnectionProfileDto ToDto(ConnectionProfile p) =>
        new(p.Id, p.Name, p.Description, p.Kind, p.SshHost, p.SshPort, p.SshUsername, p.AuthMethod,
            p.HostKeyFingerprint, p.CreatedAt, p.UpdatedAt);
}
```

Note: reflection to force-set `Id` is used for secret-path consistency. Alternative: modify `ConnectionProfile.Create` to accept an `id` parameter. If you prefer the cleaner approach, add an internal `SetId` method or an overload that takes `id`.

- [ ] **Step 3: Write controller tests (one happy-path per endpoint)**

```csharp
using System.Net;
using System.Net.Http.Json;
using PatchHound.Api.Models.AuthenticatedScans;
using Xunit;

namespace PatchHound.Tests.Api;

public class ConnectionProfilesControllerTests : IClassFixture<TestApiFactory>
{
    private readonly HttpClient _client;
    public ConnectionProfilesControllerTests(TestApiFactory f) { _client = f.CreateAuthedClient(); }

    [Fact]
    public async Task Create_stores_profile_without_persisting_password()
    {
        var req = new CreateConnectionProfileRequest(
            Name: "dc-west", Description: "", SshHost: "h.example.com", SshPort: 22, SshUsername: "root",
            AuthMethod: "password", Password: "hunter2", PrivateKey: null, PrivateKeyPassphrase: null,
            HostKeyFingerprint: null);
        var resp = await _client.PostAsJsonAsync("/api/authenticated-scans/connection-profiles", req);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<ConnectionProfileDto>();
        Assert.Equal("password", dto!.AuthMethod);
        // No password/secretRef in response DTO
        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("hunter2", body);
        Assert.DoesNotContain("SecretRef", body);
    }

    [Fact]
    public async Task List_only_returns_current_tenant_profiles()
    {
        // Relies on TestApiFactory seeding; add a row for another tenant and confirm it's not returned.
        var resp = await _client.GetAsync("/api/authenticated-scans/connection-profiles");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_rejects_when_referenced_by_scan_profile()
    {
        // (requires seeding of a ScanProfile referencing the ConnectionProfile)
        // Test body follows fixture conventions; see existing *ControllerTests for patterns.
    }
}
```

(`TestApiFactory` should follow patterns of the existing controller tests in `tests/PatchHound.Tests/Api/`. If a tenant-context fixture doesn't exist, inspect existing admin controller tests like `AssetRulesControllerTests.cs` to mirror.)

- [ ] **Step 4: Run + commit**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~ConnectionProfilesControllerTests`
Expected: all pass.

```bash
git add src/PatchHound.Api/Models/AuthenticatedScans/ConnectionProfileDtos.cs src/PatchHound.Api/Controllers/ConnectionProfilesController.cs tests/PatchHound.Tests/Api/ConnectionProfilesControllerTests.cs
git commit -m "feat: add ConnectionProfilesController with OpenBao secret handling"
```

---

## Task 15: `ScanRunnersController` with bearer-secret lifecycle

**Files:**
- Create: `src/PatchHound.Api/Models/AuthenticatedScans/ScanRunnerDtos.cs`
- Create: `src/PatchHound.Api/Controllers/ScanRunnersController.cs`
- Create: `tests/PatchHound.Tests/Api/ScanRunnersControllerTests.cs`

Endpoints: list, get, create (returns one-time secret), update, rotate-secret, delete. Authenticated Api admin endpoints only; runner-facing `/api/scan-runner/*` endpoints come in Plan 2.

- [ ] **Step 1: DTOs**

```csharp
namespace PatchHound.Api.Models.AuthenticatedScans;

public record ScanRunnerDto(
    Guid Id, string Name, string Description, bool Enabled, string Version,
    DateTimeOffset? LastSeenAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public record CreateScanRunnerRequest(string Name, string Description);
public record UpdateScanRunnerRequest(string Name, string Description, bool Enabled);

public record ScanRunnerCreatedDto(ScanRunnerDto Runner, string Secret, string RunnerYamlSnippet);
public record ScanRunnerSecretRotatedDto(string Secret);
```

- [ ] **Step 2: Controller + token hashing helper**

Create a small helper `ScanRunnerTokens` in `src/PatchHound.Infrastructure/AuthenticatedScans/ScanRunnerTokens.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace PatchHound.Infrastructure.AuthenticatedScans;

public static class ScanRunnerTokens
{
    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
    public static string Hash(string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
```

Controller:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.AuthenticatedScans;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.AuthenticatedScans;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/authenticated-scans/scan-runners")]
[Authorize(Policy = Policies.ConfigureTenant)]
public class ScanRunnersController : ControllerBase
{
    private readonly PatchHoundDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly AuditLogWriter _audit;

    public ScanRunnersController(PatchHoundDbContext db, ITenantContext tenant, AuditLogWriter audit)
    { _db = db; _tenant = tenant; _audit = audit; }

    [HttpGet]
    public async Task<IEnumerable<ScanRunnerDto>> List(CancellationToken ct) =>
        (await _db.ScanRunners.AsNoTracking()
            .Where(r => r.TenantId == _tenant.TenantId)
            .OrderBy(r => r.Name).ToListAsync(ct)).Select(ToDto);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ScanRunnerDto>> Get(Guid id, CancellationToken ct)
    {
        var r = await _db.ScanRunners.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.TenantId == _tenant.TenantId, ct);
        return r is null ? NotFound() : ToDto(r);
    }

    [HttpPost]
    public async Task<ActionResult<ScanRunnerCreatedDto>> Create([FromBody] CreateScanRunnerRequest req, CancellationToken ct)
    {
        var secret = ScanRunnerTokens.GenerateSecret();
        var runner = ScanRunner.Create(_tenant.TenantId, req.Name, req.Description, ScanRunnerTokens.Hash(secret));
        _db.ScanRunners.Add(runner);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("ScanRunner.Created", runner.Id.ToString(), ct);
        var yaml = $"tenantId: {_tenant.TenantId}\nrunnerId: {runner.Id}\nbearerSecret: {secret}\ncentralUrl: https://<your-api>\n";
        return new ScanRunnerCreatedDto(ToDto(runner), secret, yaml);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ScanRunnerDto>> Update(Guid id, [FromBody] UpdateScanRunnerRequest req, CancellationToken ct)
    {
        var r = await _db.ScanRunners.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == _tenant.TenantId, ct);
        if (r is null) return NotFound();
        r.Update(req.Name, req.Description, req.Enabled);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("ScanRunner.Updated", id.ToString(), ct);
        return ToDto(r);
    }

    [HttpPost("{id:guid}/rotate-secret")]
    public async Task<ActionResult<ScanRunnerSecretRotatedDto>> Rotate(Guid id, CancellationToken ct)
    {
        var r = await _db.ScanRunners.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == _tenant.TenantId, ct);
        if (r is null) return NotFound();
        var secret = ScanRunnerTokens.GenerateSecret();
        r.RotateSecret(ScanRunnerTokens.Hash(secret));
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("ScanRunner.SecretRotated", id.ToString(), ct);
        return new ScanRunnerSecretRotatedDto(secret);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var r = await _db.ScanRunners.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == _tenant.TenantId, ct);
        if (r is null) return NotFound();
        var inUse = await _db.ScanProfiles.AnyAsync(sp => sp.ScanRunnerId == id, ct);
        if (inUse) return Conflict("runner is referenced by a scan profile");
        _db.ScanRunners.Remove(r);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("ScanRunner.Deleted", id.ToString(), ct);
        return NoContent();
    }

    private static ScanRunnerDto ToDto(ScanRunner r) =>
        new(r.Id, r.Name, r.Description, r.Enabled, r.Version, r.LastSeenAt, r.CreatedAt, r.UpdatedAt);
}
```

- [ ] **Step 3: Tests**

Add happy-path tests for create (returns secret once, secret is not stored plaintext in DB), rotate (DB hash changes), delete-blocked-when-in-use. Follow pattern from `ConnectionProfilesControllerTests.cs`.

- [ ] **Step 4: Run + commit**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~ScanRunnersControllerTests`

```bash
git add src/PatchHound.Infrastructure/AuthenticatedScans/ScanRunnerTokens.cs src/PatchHound.Api/Models/AuthenticatedScans/ScanRunnerDtos.cs src/PatchHound.Api/Controllers/ScanRunnersController.cs tests/PatchHound.Tests/Api/ScanRunnersControllerTests.cs
git commit -m "feat: add ScanRunnersController with bearer-secret lifecycle"
```

---

## Task 16: `ScanningToolsController` using `ScanningToolVersionStore`

**Files:**
- Create: `src/PatchHound.Api/Models/AuthenticatedScans/ScanningToolDtos.cs`
- Create: `src/PatchHound.Api/Controllers/ScanningToolsController.cs`
- Create: `tests/PatchHound.Tests/Api/ScanningToolsControllerTests.cs`

CRUD over `ScanningTool`. Script content managed through a dedicated `PUT /scanning-tools/{id}/script` endpoint that calls `ScanningToolVersionStore.AppendAsync` and auto-prunes.

- [ ] **Step 1: DTOs**

```csharp
namespace PatchHound.Api.Models.AuthenticatedScans;

public record ScanningToolDto(
    Guid Id, string Name, string Description, string ScriptType, string InterpreterPath,
    int TimeoutSeconds, string OutputModel, Guid? CurrentVersionId,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public record CreateScanningToolRequest(
    string Name, string Description, string ScriptType, string InterpreterPath,
    int TimeoutSeconds, string OutputModel, string InitialScript);

public record UpdateScanningToolRequest(
    string Name, string Description, string ScriptType, string InterpreterPath, int TimeoutSeconds);

public record UpdateScriptRequest(string ScriptContent);

public record ScanningToolVersionDto(Guid Id, int VersionNumber, Guid EditedByUserId, DateTimeOffset EditedAt);

public record ScanningToolVersionContentDto(
    Guid Id, int VersionNumber, string ScriptContent, Guid EditedByUserId, DateTimeOffset EditedAt);
```

- [ ] **Step 2: Controller**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.AuthenticatedScans;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.AuthenticatedScans;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/authenticated-scans/scanning-tools")]
[Authorize(Policy = Policies.ConfigureTenant)]
public class ScanningToolsController : ControllerBase
{
    private readonly PatchHoundDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ScanningToolVersionStore _versions;
    private readonly AuditLogWriter _audit;

    public ScanningToolsController(PatchHoundDbContext db, ITenantContext tenant,
        ScanningToolVersionStore versions, AuditLogWriter audit)
    { _db = db; _tenant = tenant; _versions = versions; _audit = audit; }

    [HttpGet]
    public async Task<IEnumerable<ScanningToolDto>> List(CancellationToken ct) =>
        (await _db.ScanningTools.AsNoTracking()
            .Where(t => t.TenantId == _tenant.TenantId)
            .OrderBy(t => t.Name).ToListAsync(ct)).Select(ToDto);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ScanningToolDto>> Get(Guid id, CancellationToken ct)
    {
        var t = await _db.ScanningTools.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.TenantId == _tenant.TenantId, ct);
        return t is null ? NotFound() : ToDto(t);
    }

    [HttpPost]
    public async Task<ActionResult<ScanningToolDto>> Create([FromBody] CreateScanningToolRequest req, CancellationToken ct)
    {
        var tool = ScanningTool.Create(_tenant.TenantId, req.Name, req.Description,
            req.ScriptType, req.InterpreterPath, req.TimeoutSeconds, req.OutputModel);
        _db.ScanningTools.Add(tool);
        await _db.SaveChangesAsync(ct);
        var userId = _tenant.UserId ?? Guid.Empty;
        await _versions.AppendAsync(tool.Id, req.InitialScript, userId, ct);
        await _audit.WriteAsync("ScanningTool.Created", tool.Id.ToString(), ct);
        return CreatedAtAction(nameof(Get), new { id = tool.Id }, ToDto(tool));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ScanningToolDto>> Update(Guid id, [FromBody] UpdateScanningToolRequest req, CancellationToken ct)
    {
        var t = await _db.ScanningTools.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == _tenant.TenantId, ct);
        if (t is null) return NotFound();
        t.UpdateMetadata(req.Name, req.Description, req.ScriptType, req.InterpreterPath, req.TimeoutSeconds);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("ScanningTool.Updated", id.ToString(), ct);
        return ToDto(t);
    }

    [HttpPut("{id:guid}/script")]
    public async Task<ActionResult<ScanningToolVersionDto>> UpdateScript(Guid id, [FromBody] UpdateScriptRequest req, CancellationToken ct)
    {
        var t = await _db.ScanningTools.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == _tenant.TenantId, ct);
        if (t is null) return NotFound();
        var userId = _tenant.UserId ?? Guid.Empty;
        var v = await _versions.AppendAsync(id, req.ScriptContent, userId, ct);
        await _audit.WriteAsync("ScanningTool.ScriptUpdated", id.ToString(), ct);
        return new ScanningToolVersionDto(v.Id, v.VersionNumber, v.EditedByUserId, v.EditedAt);
    }

    [HttpGet("{id:guid}/versions")]
    public async Task<ActionResult<IEnumerable<ScanningToolVersionDto>>> ListVersions(Guid id, CancellationToken ct)
    {
        var tool = await _db.ScanningTools.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.TenantId == _tenant.TenantId, ct);
        if (tool is null) return NotFound();
        var versions = await _db.ScanningToolVersions.AsNoTracking()
            .Where(v => v.ScanningToolId == id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new ScanningToolVersionDto(v.Id, v.VersionNumber, v.EditedByUserId, v.EditedAt))
            .ToListAsync(ct);
        return Ok(versions);
    }

    [HttpGet("{id:guid}/versions/{versionId:guid}")]
    public async Task<ActionResult<ScanningToolVersionContentDto>> GetVersion(Guid id, Guid versionId, CancellationToken ct)
    {
        var tool = await _db.ScanningTools.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.TenantId == _tenant.TenantId, ct);
        if (tool is null) return NotFound();
        var v = await _db.ScanningToolVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == versionId && x.ScanningToolId == id, ct);
        return v is null ? NotFound() : new ScanningToolVersionContentDto(v.Id, v.VersionNumber, v.ScriptContent, v.EditedByUserId, v.EditedAt);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var t = await _db.ScanningTools.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == _tenant.TenantId, ct);
        if (t is null) return NotFound();
        var inUse = await _db.ScanProfileTools.AnyAsync(pt => pt.ScanningToolId == id, ct);
        if (inUse) return Conflict("tool is referenced by a scan profile");
        _db.ScanningToolVersions.RemoveRange(_db.ScanningToolVersions.Where(v => v.ScanningToolId == id));
        _db.ScanningTools.Remove(t);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("ScanningTool.Deleted", id.ToString(), ct);
        return NoContent();
    }

    private static ScanningToolDto ToDto(ScanningTool t) =>
        new(t.Id, t.Name, t.Description, t.ScriptType, t.InterpreterPath, t.TimeoutSeconds,
            t.OutputModel, t.CurrentVersionId, t.CreatedAt, t.UpdatedAt);
}
```

Note: `_tenant.UserId` — confirm the existing `ITenantContext` exposes a user id (search for it). If the field is named differently (e.g. `CurrentUserId`), adjust.

- [ ] **Step 3: Controller tests (happy path per endpoint)**

At minimum: create (stores initial version), update-script (creates v2), list-versions (returns newest first), delete-blocked-when-referenced.

- [ ] **Step 4: Run + commit**

```bash
dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~ScanningToolsControllerTests
git add src/PatchHound.Api/Models/AuthenticatedScans/ScanningToolDtos.cs src/PatchHound.Api/Controllers/ScanningToolsController.cs tests/PatchHound.Tests/Api/ScanningToolsControllerTests.cs
git commit -m "feat: add ScanningToolsController with script versioning"
```

---

## Task 17: `ScanProfilesController` (incl. tool assignment + manual trigger request)

**Files:**
- Create: `src/PatchHound.Api/Models/AuthenticatedScans/ScanProfileDtos.cs`
- Create: `src/PatchHound.Api/Controllers/ScanProfilesController.cs`
- Create: `tests/PatchHound.Tests/Api/ScanProfilesControllerTests.cs`

CRUD + `POST /{id}/trigger-run` (writes `ManualRequestedAt` — actual scheduler firing comes in Plan 2).

- [ ] **Step 1: DTOs**

```csharp
namespace PatchHound.Api.Models.AuthenticatedScans;

public record ScanProfileToolDto(Guid ScanningToolId, int ExecutionOrder);

public record ScanProfileDto(
    Guid Id, string Name, string Description, string CronSchedule,
    Guid ConnectionProfileId, Guid ScanRunnerId, bool Enabled,
    DateTimeOffset? ManualRequestedAt, DateTimeOffset? LastRunStartedAt,
    IReadOnlyList<ScanProfileToolDto> Tools,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public record CreateScanProfileRequest(
    string Name, string Description, string CronSchedule,
    Guid ConnectionProfileId, Guid ScanRunnerId, bool Enabled,
    IReadOnlyList<ScanProfileToolDto> Tools);

public record UpdateScanProfileRequest(
    string Name, string Description, string CronSchedule,
    Guid ConnectionProfileId, Guid ScanRunnerId, bool Enabled,
    IReadOnlyList<ScanProfileToolDto> Tools);
```

- [ ] **Step 2: Controller**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.AuthenticatedScans;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Interfaces;
using PatchHound.Infrastructure.Data;
using PatchHound.Infrastructure.Services;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/authenticated-scans/scan-profiles")]
[Authorize(Policy = Policies.ConfigureTenant)]
public class ScanProfilesController : ControllerBase
{
    private readonly PatchHoundDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly AuditLogWriter _audit;

    public ScanProfilesController(PatchHoundDbContext db, ITenantContext tenant, AuditLogWriter audit)
    { _db = db; _tenant = tenant; _audit = audit; }

    [HttpGet]
    public async Task<IEnumerable<ScanProfileDto>> List(CancellationToken ct)
    {
        var items = await _db.ScanProfiles.AsNoTracking()
            .Where(p => p.TenantId == _tenant.TenantId)
            .OrderBy(p => p.Name).ToListAsync(ct);
        var toolsByProfile = (await _db.ScanProfileTools.AsNoTracking()
            .Where(t => items.Select(p => p.Id).Contains(t.ScanProfileId))
            .ToListAsync(ct)).GroupBy(t => t.ScanProfileId).ToDictionary(g => g.Key, g => g.ToList());
        return items.Select(p => ToDto(p, toolsByProfile.GetValueOrDefault(p.Id) ?? new()));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ScanProfileDto>> Get(Guid id, CancellationToken ct)
    {
        var p = await _db.ScanProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.TenantId == _tenant.TenantId, ct);
        if (p is null) return NotFound();
        var tools = await _db.ScanProfileTools.AsNoTracking().Where(t => t.ScanProfileId == id).ToListAsync(ct);
        return ToDto(p, tools);
    }

    [HttpPost]
    public async Task<ActionResult<ScanProfileDto>> Create([FromBody] CreateScanProfileRequest req, CancellationToken ct)
    {
        await ValidateReferencesAsync(req.ConnectionProfileId, req.ScanRunnerId, req.Tools.Select(t => t.ScanningToolId), ct);
        var p = ScanProfile.Create(_tenant.TenantId, req.Name, req.Description, req.CronSchedule,
            req.ConnectionProfileId, req.ScanRunnerId, req.Enabled);
        _db.ScanProfiles.Add(p);
        foreach (var t in req.Tools)
            _db.ScanProfileTools.Add(ScanProfileTool.Create(p.Id, t.ScanningToolId, t.ExecutionOrder));
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("ScanProfile.Created", p.Id.ToString(), ct);
        return CreatedAtAction(nameof(Get), new { id = p.Id }, ToDto(p, await _db.ScanProfileTools.Where(t => t.ScanProfileId == p.Id).ToListAsync(ct)));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ScanProfileDto>> Update(Guid id, [FromBody] UpdateScanProfileRequest req, CancellationToken ct)
    {
        var p = await _db.ScanProfiles.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == _tenant.TenantId, ct);
        if (p is null) return NotFound();
        await ValidateReferencesAsync(req.ConnectionProfileId, req.ScanRunnerId, req.Tools.Select(t => t.ScanningToolId), ct);
        p.Update(req.Name, req.Description, req.CronSchedule, req.ConnectionProfileId, req.ScanRunnerId, req.Enabled);
        _db.ScanProfileTools.RemoveRange(_db.ScanProfileTools.Where(t => t.ScanProfileId == id));
        foreach (var t in req.Tools)
            _db.ScanProfileTools.Add(ScanProfileTool.Create(id, t.ScanningToolId, t.ExecutionOrder));
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("ScanProfile.Updated", id.ToString(), ct);
        var tools = await _db.ScanProfileTools.AsNoTracking().Where(t => t.ScanProfileId == id).ToListAsync(ct);
        return ToDto(p, tools);
    }

    [HttpPost("{id:guid}/trigger-run")]
    public async Task<IActionResult> TriggerRun(Guid id, CancellationToken ct)
    {
        var p = await _db.ScanProfiles.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == _tenant.TenantId, ct);
        if (p is null) return NotFound();
        var active = await _db.AuthenticatedScanRuns.AnyAsync(r => r.ScanProfileId == id
            && r.Status != AuthenticatedScanRunStatuses.Succeeded
            && r.Status != AuthenticatedScanRunStatuses.Failed
            && r.Status != AuthenticatedScanRunStatuses.PartiallyFailed, ct);
        if (active) return Conflict("profile already has an active run");
        p.RequestManualRun(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("ScanProfile.ManualTriggerRequested", id.ToString(), ct);
        return Accepted();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var p = await _db.ScanProfiles.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == _tenant.TenantId, ct);
        if (p is null) return NotFound();
        _db.ScanProfileTools.RemoveRange(_db.ScanProfileTools.Where(t => t.ScanProfileId == id));
        _db.AssetScanProfileAssignments.RemoveRange(_db.AssetScanProfileAssignments.Where(a => a.ScanProfileId == id));
        _db.ScanProfiles.Remove(p);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("ScanProfile.Deleted", id.ToString(), ct);
        return NoContent();
    }

    private async Task ValidateReferencesAsync(Guid connId, Guid runnerId, IEnumerable<Guid> toolIds, CancellationToken ct)
    {
        if (!await _db.ConnectionProfiles.AnyAsync(c => c.Id == connId && c.TenantId == _tenant.TenantId, ct))
            throw new BadHttpRequestException("invalid connectionProfileId");
        if (!await _db.ScanRunners.AnyAsync(r => r.Id == runnerId && r.TenantId == _tenant.TenantId, ct))
            throw new BadHttpRequestException("invalid scanRunnerId");
        var ids = toolIds.ToList();
        var found = await _db.ScanningTools.CountAsync(t => ids.Contains(t.Id) && t.TenantId == _tenant.TenantId, ct);
        if (found != ids.Count) throw new BadHttpRequestException("one or more invalid scanningToolIds");
    }

    private static ScanProfileDto ToDto(ScanProfile p, List<ScanProfileTool> tools) =>
        new(p.Id, p.Name, p.Description, p.CronSchedule, p.ConnectionProfileId, p.ScanRunnerId, p.Enabled,
            p.ManualRequestedAt, p.LastRunStartedAt,
            tools.OrderBy(t => t.ExecutionOrder).Select(t => new ScanProfileToolDto(t.ScanningToolId, t.ExecutionOrder)).ToList(),
            p.CreatedAt, p.UpdatedAt);
}
```

- [ ] **Step 3: Controller tests**

Happy path: create with tools, update to re-order tools, trigger-run writes ManualRequestedAt, trigger-run returns 409 when an active run already exists, invalid references return 400, delete cascades tools and assignments.

- [ ] **Step 4: Run + commit**

```bash
dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~ScanProfilesControllerTests
git add src/PatchHound.Api/Models/AuthenticatedScans/ScanProfileDtos.cs src/PatchHound.Api/Controllers/ScanProfilesController.cs tests/PatchHound.Tests/Api/ScanProfilesControllerTests.cs
git commit -m "feat: add ScanProfilesController with tool assignment and manual trigger"
```

---

## Task 18: Asset-rule `AssignScanProfile` operation — evaluation wiring + tests

**Files:**
- Modify: wherever `IAssetRuleEvaluationService` is implemented (search below)
- Create: `tests/PatchHound.Tests/Core/AssetRuleEvaluationAssignScanProfileTests.cs`

When an asset rule has operation `{ "type": "AssignScanProfile", "parameters": {"scanProfileId": "<guid>"} }`, evaluating it inserts/upserts an `AssetScanProfileAssignment` for every matched asset.

- [ ] **Step 1: Locate the evaluation service**

Run: `grep -rn "IAssetRuleEvaluationService" src/ --include="*.cs"`
Open the implementation — likely `src/PatchHound.Core/Services/AssetRuleEvaluationService.cs` or `src/PatchHound.Infrastructure/Services/`. Find the operation dispatch (likely a switch on `operation.Type`). Note the file path.

- [ ] **Step 2: Write failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Models;
using PatchHound.Infrastructure.Data;
using Xunit;

namespace PatchHound.Tests.Core;

public class AssetRuleEvaluationAssignScanProfileTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private Guid _tenantId = Guid.NewGuid();
    private Guid _assetId;
    private Guid _scanProfileId = Guid.NewGuid();
    private Guid _ruleId;

    public async Task InitializeAsync()
    {
        _db = TestDbContextFactory.CreateInMemory();
        _assetId = await TestData.SeedDevice(_db, _tenantId, "h1");
        // seed a ScanProfile stub (enough FKs to satisfy constraints, adjust to test data factories)
        _db.ConnectionProfiles.Add(ConnectionProfile.Create(_tenantId, "c", "", "h", 22, "u", "password", "p", null));
        _db.ScanRunners.Add(ScanRunner.Create(_tenantId, "r", "", "hash"));
        var prof = ScanProfile.Create(_tenantId, "p", "", "", Guid.NewGuid(), Guid.NewGuid(), true);
        typeof(ScanProfile).GetProperty(nameof(ScanProfile.Id))!.SetValue(prof, _scanProfileId);
        _db.ScanProfiles.Add(prof);
        // Simplest filter: match all assets for this tenant
        var rule = AssetRule.Create(
            tenantId: _tenantId, name: "all", description: null, priority: 0,
            filter: new FilterGroup("AND", new List<FilterNode>()),
            operations: new List<AssetRuleOperation>
            {
                new("AssignScanProfile", new Dictionary<string,string> { ["scanProfileId"] = _scanProfileId.ToString() }),
            });
        _db.AssetRules.Add(rule);
        _ruleId = rule.Id;
        await _db.SaveChangesAsync();
    }
    public Task DisposeAsync() { _db.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Evaluating_rule_inserts_assignment_for_matched_assets()
    {
        var service = new AssetRuleEvaluationService(_db /*, other deps */);
        await service.EvaluateRuleAsync(_ruleId, CancellationToken.None);

        var assignment = await _db.AssetScanProfileAssignments.SingleAsync();
        Assert.Equal(_assetId, assignment.AssetId);
        Assert.Equal(_scanProfileId, assignment.ScanProfileId);
        Assert.Equal(_ruleId, assignment.AssignedByRuleId);
    }

    [Fact]
    public async Task Evaluating_rule_twice_does_not_duplicate_assignment()
    {
        var service = new AssetRuleEvaluationService(_db);
        await service.EvaluateRuleAsync(_ruleId, CancellationToken.None);
        await service.EvaluateRuleAsync(_ruleId, CancellationToken.None);
        Assert.Equal(1, await _db.AssetScanProfileAssignments.CountAsync());
    }
}
```

Adapt the service constructor + method signature to the real one found in Step 1.

- [ ] **Step 3: Run failing test**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~AssetRuleEvaluationAssignScanProfileTests`
Expected: FAIL.

- [ ] **Step 4: Add the `"AssignScanProfile"` case to the operation handler**

In the evaluation service's operation switch, add:

```csharp
case "AssignScanProfile":
    if (!operation.Parameters.TryGetValue("scanProfileId", out var profileStr) || !Guid.TryParse(profileStr, out var scanProfileId))
        break; // or log skip
    foreach (var assetId in matchedAssetIds)
    {
        var exists = await _db.AssetScanProfileAssignments
            .AnyAsync(a => a.AssetId == assetId && a.ScanProfileId == scanProfileId, ct);
        if (!exists)
        {
            _db.AssetScanProfileAssignments.Add(
                AssetScanProfileAssignment.Create(rule.TenantId, assetId, scanProfileId, rule.Id));
        }
    }
    break;
```

(Adapt variable names `matchedAssetIds`, `rule`, `_db`, `ct` to match the real service's code.)

- [ ] **Step 5: Run tests until green**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~AssetRuleEvaluationAssignScanProfileTests`

- [ ] **Step 6: Commit**

```bash
git add <evaluation-service-file> tests/PatchHound.Tests/Core/AssetRuleEvaluationAssignScanProfileTests.cs
git commit -m "feat: support AssignScanProfile operation in asset rule evaluation"
```

---

## Task 19: End-to-end backend sanity test

**Files:**
- Create: `tests/PatchHound.Tests/Infrastructure/AuthenticatedScans/BackendEndToEndTests.cs`

One high-level test that wires up connection profile → tool → scan profile → assignment → dispatcher → fake result → ingestion service → assert installation exists.

- [ ] **Step 1: Write the test**

```csharp
using Microsoft.EntityFrameworkCore;
using PatchHound.Core.Entities.AuthenticatedScans;
using PatchHound.Core.Enums;
using PatchHound.Infrastructure.AuthenticatedScans;
using PatchHound.Infrastructure.Data;
using Xunit;

namespace PatchHound.Tests.Infrastructure.AuthenticatedScans;

public class BackendEndToEndTests : IAsyncLifetime
{
    private PatchHoundDbContext _db = null!;
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync() { _db = TestDbContextFactory.CreateInMemory(); await Task.CompletedTask; }
    public Task DisposeAsync() { _db.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task Full_backend_flow_dispatches_and_ingests_software()
    {
        // Arrange: seed all config
        var asset = await TestData.SeedDevice(_db, _tenantId, "host-1");
        var conn = ConnectionProfile.Create(_tenantId, "c1", "", "h", 22, "u", "password", "secrets/tenants/x/conn/y", null);
        var runner = ScanRunner.Create(_tenantId, "r1", "", "hash");
        var tool = ScanningTool.Create(_tenantId, "t1", "", "python", "/usr/bin/python3", 300, "NormalizedSoftware");
        var vStore = new ScanningToolVersionStore(_db);
        _db.ConnectionProfiles.Add(conn); _db.ScanRunners.Add(runner); _db.ScanningTools.Add(tool);
        await _db.SaveChangesAsync();
        await vStore.AppendAsync(tool.Id, "print('{}')", Guid.NewGuid(), CancellationToken.None);
        var profile = ScanProfile.Create(_tenantId, "p1", "", "", conn.Id, runner.Id, true);
        _db.ScanProfiles.Add(profile);
        _db.ScanProfileTools.Add(ScanProfileTool.Create(profile.Id, tool.Id, 0));
        _db.AssetScanProfileAssignments.Add(AssetScanProfileAssignment.Create(_tenantId, asset, profile.Id, null));
        await _db.SaveChangesAsync();

        // Act: dispatcher creates jobs
        var dispatcher = new ScanJobDispatcher(_db);
        var runId = await dispatcher.StartRunAsync(profile.Id, "manual", Guid.NewGuid(), CancellationToken.None);
        var job = await _db.ScanJobs.SingleAsync(j => j.RunId == runId);
        _db.ScanJobResults.Add(ScanJobResult.Create(job.Id, "ok", "",
            """{"software":[{"canonicalName":"nginx","canonicalProductKey":"nginx:nginx","detectedVersion":"1.24.0"}]}"""));
        await _db.SaveChangesAsync();

        // Act: ingest the job
        var ingest = new AuthenticatedScanIngestionService(_db, new AuthenticatedScanOutputValidator());
        var summary = await ingest.IngestAsync(job.Id, profile.Id, CancellationToken.None);

        // Assert
        Assert.Equal(1, summary.EntriesIngested);
        Assert.True(await _db.NormalizedSoftwareInstallations
            .AnyAsync(i => i.DeviceAssetId == asset && i.SourceSystem == SoftwareIdentitySourceSystem.AuthenticatedScan));
    }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test tests/PatchHound.Tests --filter FullyQualifiedName~BackendEndToEndTests`
Expected: passes.

- [ ] **Step 3: Commit**

```bash
git add tests/PatchHound.Tests/Infrastructure/AuthenticatedScans/BackendEndToEndTests.cs
git commit -m "test: add backend end-to-end sanity test for authenticated scans"
```

---

## Task 20: Final build + test sweep

- [ ] **Step 1: Full solution build**

Run: `dotnet build PatchHound.slnx`
Expected: succeeds with no errors or new warnings.

- [ ] **Step 2: Full test run**

Run: `dotnet test tests/PatchHound.Tests`
Expected: all tests pass (pre-existing + new ones).

- [ ] **Step 3: Verify no plaintext secrets anywhere**

Run: `grep -rn "Password\|SecretRef" src/PatchHound.Api/Models/AuthenticatedScans/`
Confirm no DTO exposes `SecretRef`, `Password`, `PrivateKey`, or `Passphrase` in response shapes. Only request DTOs should have them.

- [ ] **Step 4: Verify EF migration applies cleanly from scratch**

Run (if you maintain a dev compose / test container):
```bash
dotnet ef database drop --force --startup-project src/PatchHound.Api --project src/PatchHound.Infrastructure
dotnet ef database update --startup-project src/PatchHound.Api --project src/PatchHound.Infrastructure
```
Expected: applies without error.

- [ ] **Step 5: Commit any final cleanups** (if any)

```bash
git commit --allow-empty -m "chore: backend foundations for authenticated scans complete"
```

---

## Self-Review (plan author)

**Spec coverage:**
- §4.1 Configuration entities → Tasks 2, 3, 4, 5
- §4.2 Run/execution entities → Task 6
- §4.3 Enum + AssetRule op → Tasks 1, 18
- §5.1 Enrollment (runner create + secret once) → Task 15 (admin-side only; runner-side `/scan-runner/heartbeat` is Plan 2)
- §6.2 ScanJobDispatcher.StartRun → Task 12
- §7.1 Output contract → documented via validator tests (Task 10)
- §7.2 Validation rules → Task 10
- §7.3 Staging + merge + precedence → Task 11
- §8 UI → **deferred to Plan 3** (out of scope here, per plan split)
- §9 Security (credentials in OpenBao, hashed runner secrets, audit via existing interceptor, tenant scoping, size caps) → Tasks 9, 11, 14, 15

**Confirmed out of scope for this plan** (tracked in subsequent plans):
- `ScanSchedulerWorker` + cron ticking + stale sweeper → Plan 2
- `/api/scan-runner/*` endpoints (pull jobs, heartbeat, post results) → Plan 2
- `PatchHound.ScanRunner` binary → Plan 2
- Run completion detection on result post → Plan 2
- Admin workbench UI → Plan 3
- Sources admin tab + reports + asset-rules UI → Plan 4

**Type consistency:** `AuthenticatedScanRunStatuses`, `ScanJobStatuses` constants used consistently across entities, dispatcher, ingestion service, and controllers. `SoftwareIdentitySourceSystem.AuthenticatedScan` used consistently. `ConnectionProfile.Id` reused as OpenBao path segment (matched via reflection-set or Create-with-id).

**Known gaps/decisions surfaced to implementer:**
- Task 11 Step 1: the exact shape of the `EnsureTenantSoftwareAsync` call depends on existing Defender merge logic that must be located and possibly extracted.
- Task 14 Step 2: reflection-set of `ConnectionProfile.Id` is a temporary measure; the implementer may choose to add an overload `Create(id, tenantId, …)` instead.
- Task 16 Step 2: `_tenant.UserId` availability depends on the existing `ITenantContext` shape.
- Task 18 Step 1: asset-rule evaluation service location requires `grep` at implementation time.
