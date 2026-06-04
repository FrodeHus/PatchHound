# AI Operational Context — Phase 6: Audit Persistence of Grounded Recommendations

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make AI-assisted recommendations auditable after the fact. When a grounded recommendation draft is generated, persist the exact context pack + hash + the model's validated citations as a tenant-scoped snapshot row; when the analyst saves a recommendation, link that snapshot to the saved `AnalystRecommendation`; expose a retrieval endpoint so an auditor can later see exactly which local facts informed the decision.

**Architecture:** Capture-at-generation, link-on-save (server-authoritative, untamperable). `AiRecommendationDraftService` writes a `RecommendationContextSnapshot` (tenant-scoped, carries `TenantId`+`RemediationCaseId`) at generation time and returns its id in the draft DTO. The save endpoint accepts that id, verifies it belongs to the same tenant+case, and links it to the `AnalystRecommendation`. A GET endpoint returns the stored pack + citations for audit. Snapshots from discarded drafts become orphans (a periodic cleanup of unlinked snapshots older than N days is a noted follow-up, not in this plan).

**Tech Stack:** .NET / C#, EF Core (PostgreSQL), xunit + FluentAssertions + NSubstitute, Testcontainers; React 19 (`frontend/`). Spec: `docs/superpowers/specs/2026-06-01-local-ai-operational-context-rag-design.md` (auditability acceptance criterion: "the exact context snapshot used for that generation is available"). Builds on merged Phases 1–5.

**Scope note:** This is tenant-safe by construction — `RecommendationContextSnapshot` and `AnalystRecommendation` both carry `TenantId` (unlike the global `VulnerabilityPatchAssessment`). **Deferred:** a frontend "view recorded context" panel on the saved recommendation (the backend GET endpoint satisfies the spec criterion via API); orphan-snapshot cleanup job; applying the same persistence to `AIReport`/other generators.

---

## File Structure

**Create (Core):**
- `src/PatchHound.Core/Entities/RecommendationContextSnapshot.cs` — tenant-scoped snapshot entity + factory.

**Modify (Core):**
- `src/PatchHound.Core/Entities/AnalystRecommendation.cs` — add `ContextSnapshotId` + thread through `Create`/`Update`.

**Create (Infrastructure):**
- `src/PatchHound.Infrastructure/Data/Configurations/RecommendationContextSnapshotConfiguration.cs`

**Modify (Infrastructure):**
- `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs` — `DbSet` + global tenant query filter.
- `src/PatchHound.Infrastructure/Data/Configurations/AnalystRecommendationConfiguration.cs` (if exists) — optional FK.
- `src/PatchHound.Infrastructure/Services/AnalystRecommendationService.cs` — accept + verify + link the snapshot.
- New EF migration.

**Modify (Api):**
- `src/PatchHound.Api/Services/AiRecommendationDraftService.cs` — persist snapshot; return id; take userId.
- `src/PatchHound.Api/Models/Decisions/RemediationDecisionDto.cs` — DTO fields (`ContextSnapshotId` on draft, request, read; new snapshot DTO).
- `src/PatchHound.Api/Controllers/RemediationDecisionsController.cs` — pass userId + snapshot id; new GET endpoint.

**Modify (Frontend):**
- `frontend/src/api/remediation.schemas.ts` + `remediation.functions.ts` — draft/save schema fields.
- `frontend/src/components/features/remediation/SecurityAnalystWorkbench.tsx` — carry `contextSnapshotId` from draft into the save call.

**Create (Tests):** entity factory tests; draft-service persistence test; recommendation-service link + isolation test; controller retrieval test.

---

## Task 1: Snapshot entity + recommendation link + migration

**Files:**
- Create: `src/PatchHound.Core/Entities/RecommendationContextSnapshot.cs`
- Modify: `src/PatchHound.Core/Entities/AnalystRecommendation.cs`
- Create: `src/PatchHound.Infrastructure/Data/Configurations/RecommendationContextSnapshotConfiguration.cs`
- Modify: `src/PatchHound.Infrastructure/Data/PatchHoundDbContext.cs`
- Test: `tests/PatchHound.Tests/Core/OperationalContext/RecommendationContextSnapshotTests.cs`

- [ ] **Step 1: Write the failing entity test**

```csharp
using FluentAssertions;
using PatchHound.Core.Entities;
using Xunit;

namespace PatchHound.Tests.Core.OperationalContext;

public class RecommendationContextSnapshotTests
{
    private static RecommendationContextSnapshot New(string hash = "abc") =>
        RecommendationContextSnapshot.Create(
            tenantId: Guid.NewGuid(), remediationCaseId: Guid.NewGuid(),
            contextJson: "{\"contextKind\":\"RemediationCase\"}", contextHash: hash,
            citationsJson: "[]", generatedBy: Guid.NewGuid());

    [Fact]
    public void Create_sets_fields_and_timestamp()
    {
        var s = New();
        s.Id.Should().NotBeEmpty();
        s.ContextJson.Should().Contain("RemediationCase");
        s.GeneratedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_rejects_hash_over_64_chars()
    {
        var act = () => New(hash: new string('a', 65));
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Create_rejects_empty_tenant(string tenant)
    {
        var act = () => RecommendationContextSnapshot.Create(
            Guid.Parse(tenant), Guid.NewGuid(), "{}", "h", "[]", Guid.NewGuid());
        act.Should().Throw<ArgumentException>();
    }
}
```

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~RecommendationContextSnapshotTests" -v minimal` → FAIL (type missing).

- [ ] **Step 2: Create the entity (factory enforces caps + FK validity per CLAUDE.md)**

```csharp
namespace PatchHound.Core.Entities;

public class RecommendationContextSnapshot
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RemediationCaseId { get; private set; }
    public string ContextJson { get; private set; } = null!;
    public string ContextHash { get; private set; } = null!;
    public string CitationsJson { get; private set; } = "[]";
    public Guid GeneratedBy { get; private set; }
    public DateTimeOffset GeneratedAt { get; private set; }

    private RecommendationContextSnapshot() { }

    public static RecommendationContextSnapshot Create(
        Guid tenantId,
        Guid remediationCaseId,
        string contextJson,
        string contextHash,
        string citationsJson,
        Guid generatedBy)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (remediationCaseId == Guid.Empty)
            throw new ArgumentException("RemediationCaseId is required.", nameof(remediationCaseId));
        if (generatedBy == Guid.Empty)
            throw new ArgumentException("GeneratedBy is required.", nameof(generatedBy));
        if (string.IsNullOrWhiteSpace(contextJson))
            throw new ArgumentException("ContextJson is required.", nameof(contextJson));
        if (contextHash is { Length: > 64 })
            throw new ArgumentException("ContextHash must be at most 64 characters.", nameof(contextHash));

        return new RecommendationContextSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RemediationCaseId = remediationCaseId,
            ContextJson = contextJson,
            ContextHash = contextHash ?? string.Empty,
            CitationsJson = string.IsNullOrWhiteSpace(citationsJson) ? "[]" : citationsJson,
            GeneratedBy = generatedBy,
            GeneratedAt = DateTimeOffset.UtcNow,
        };
    }
}
```

- [ ] **Step 3: Add `ContextSnapshotId` to `AnalystRecommendation`**

Add property `public Guid? ContextSnapshotId { get; private set; }`. Add a trailing optional param `Guid? contextSnapshotId = null` to BOTH `Create` and `Update`; in `Create`'s initializer add `ContextSnapshotId = contextSnapshotId,`; in `Update` add `ContextSnapshotId = contextSnapshotId;`.

- [ ] **Step 4: EF config + DbSet + query filter**

Create `RecommendationContextSnapshotConfiguration`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatchHound.Core.Entities;

namespace PatchHound.Infrastructure.Data.Configurations;

public class RecommendationContextSnapshotConfiguration
    : IEntityTypeConfiguration<RecommendationContextSnapshot>
{
    public void Configure(EntityTypeBuilder<RecommendationContextSnapshot> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.TenantId, x.RemediationCaseId });
        builder.Property(x => x.ContextJson).HasColumnType("text").IsRequired();
        builder.Property(x => x.ContextHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CitationsJson).HasColumnType("text").IsRequired();
    }
}
```

In `PatchHoundDbContext.cs`: add `public DbSet<RecommendationContextSnapshot> RecommendationContextSnapshots => Set<RecommendationContextSnapshot>();` and a tenant query filter mirroring the other tenant-scoped entities: `.HasQueryFilter(e => IsSystemContext || AccessibleTenantIds.Contains(e.TenantId))` (apply it in the same place/way the existing entities register their filters — read that method and follow the exact pattern). Add `AnalystRecommendation.ContextSnapshotId` as an optional relationship if the config file exists (no cascade — `OnDelete(DeleteBehavior.SetNull)` or just a plain nullable scalar column with no FK constraint if simpler/consistent with the codebase; prefer a plain nullable `Guid?` column without a navigation to avoid coupling, matching how other snapshot-style ids are stored — verify against an existing optional id on a sibling entity).

- [ ] **Step 5: Generate the migration**

```bash
dotnet ef migrations add AddRecommendationContextSnapshot --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api
```
Confirm the `Up()` creates the `RecommendationContextSnapshots` table and adds `ContextSnapshotId` to `AnalystRecommendations`, and nothing else. `Down()` reverses both.

- [ ] **Step 6: Run green**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~RecommendationContextSnapshotTests" -v minimal` then `dotnet build PatchHound.slnx -v minimal`.
Expected: PASS / clean.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(ai-context): add recommendation context snapshot entity and link"
```

---

## Task 2: Persist the snapshot at generation + return its id

**Files:**
- Modify: `src/PatchHound.Api/Services/AiRecommendationDraftService.cs`
- Modify: `src/PatchHound.Api/Models/Decisions/RemediationDecisionDto.cs`
- Modify: `src/PatchHound.Api/Controllers/RemediationDecisionsController.cs`
- Test: extend `tests/PatchHound.Tests/Api/AiRecommendationDraftServiceTests.cs`

- [ ] **Step 1: Add `ContextSnapshotId` to the draft DTO**

In `RemediationDecisionDto.cs`, add a trailing defaulted field to `AiRecommendationDraftDto`: `Guid? ContextSnapshotId = null`.

- [ ] **Step 2: Write the failing test**

Add to `AiRecommendationDraftServiceTests.cs` a test `GenerateAsync_persists_context_snapshot_when_grounded`: with a profile allowing operational context and a faked context service returning a pack (PackJson `{"contextKind":"RemediationCase"}`, one citation), and a faked AI text generation returning a valid draft JSON, call `GenerateAsync(tenantId, caseId, userId, ct)`; assert the returned DTO's `ContextSnapshotId` is non-null AND a `RecommendationContextSnapshot` row exists in the (real/in-memory) dbContext for that tenant+case with `ContextJson` == the pack JSON and a 64-char `ContextHash`. Also assert `GenerateAsync` does NOT persist a snapshot when the profile disallows context (`ContextSnapshotId == null`, no rows). (Match the existing test harness — it uses a real/seeded `PatchHoundDbContext`; reuse it.)

Run the filter → FAIL (signature/behavior absent).

- [ ] **Step 3: Implement**

Change `GenerateAsync` to take `Guid userId` (after `caseId`). After building `context` (when non-null), compute the hash and persist a snapshot BEFORE returning:

```csharp
        Guid? contextSnapshotId = null;
        if (context is not null)
        {
            var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(context.PackJson))).ToLowerInvariant();
            var citationsJson = System.Text.Json.JsonSerializer.Serialize(
                validation.Citations,
                OperationalContextPack.SerializerOptions);
            var snapshot = RecommendationContextSnapshot.Create(
                tenantId, caseId, context.PackJson, hash, citationsJson, userId);
            await dbContext.RecommendationContextSnapshots.AddAsync(snapshot, ct);
            await dbContext.SaveChangesAsync(ct);
            contextSnapshotId = snapshot.Id;
        }
```

Add `contextSnapshotId` to the returned `AiRecommendationDraftDto` (final positional/named arg). Add the `using PatchHound.Core.Entities;` import. Note: compute `validation` (already in the service from Phase 4) before this block so `validation.Citations` is available; if ordering requires, persist the snapshot after `validation` is computed.

- [ ] **Step 4: Update the controller to pass `userId`**

In `GenerateAiRecommendationDraft`, change the call to `aiRecommendationDraftService.GenerateAsync(tenantId, caseId, tenantContext.CurrentUserId, ct)`.

- [ ] **Step 5: Run green**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~AiRecommendationDraftServiceTests" -v minimal` and `dotnet build PatchHound.slnx -v minimal`.
Expected: PASS (update any other caller/test of `GenerateAsync` for the new `userId` arg).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(ai-context): persist context snapshot when generating grounded draft"
```

---

## Task 3: Link the snapshot on save (verified)

**Files:**
- Modify: `src/PatchHound.Api/Models/Decisions/RemediationDecisionDto.cs` (`CreateRecommendationRequest`)
- Modify: `src/PatchHound.Infrastructure/Services/AnalystRecommendationService.cs`
- Modify: `src/PatchHound.Api/Controllers/RemediationDecisionsController.cs`
- Test: `tests/PatchHound.Tests/Infrastructure/Services/AnalystRecommendationServiceTests.cs` (extend or create)

- [ ] **Step 1: Add `ContextSnapshotId` to the request**

`CreateRecommendationRequest` gets a trailing `Guid? ContextSnapshotId = null`.

- [ ] **Step 2: Write the failing test**

In a DB-backed test (Postgres fixture), seed a tenant + remediation case + a `RecommendationContextSnapshot` for (tenant, case). Call `AddRecommendationForCaseAsync(tenant, case, outcome, rationale, analyst, vulnerabilityId: null, priorityOverride: null, contextSnapshotId: snapshot.Id, ct)`; assert the saved `AnalystRecommendation.ContextSnapshotId == snapshot.Id`. Add a second test: a snapshot belonging to a DIFFERENT case (or tenant) is NOT linked (the saved recommendation's `ContextSnapshotId` stays null) — the service must verify ownership before linking.

Run → FAIL (param absent).

- [ ] **Step 3: Implement the verified link**

Add `Guid? contextSnapshotId = null` to `AddRecommendationForCaseAsync` (before `ct`). Before creating/updating the recommendation, resolve the verified id:

```csharp
        Guid? verifiedSnapshotId = null;
        if (contextSnapshotId is Guid snapId)
        {
            var ok = await dbContext.RecommendationContextSnapshots
                .AnyAsync(s => s.Id == snapId
                    && s.TenantId == tenantId
                    && s.RemediationCaseId == remediationCaseId, ct);
            if (ok) verifiedSnapshotId = snapId;
        }
```

Pass `verifiedSnapshotId` to both `AnalystRecommendation.Create(...)` (new trailing arg) and the `recommendation.Update(...)` path. (Invalid/foreign ids are ignored — the recommendation still saves, just without a link — so a bad client value never blocks a legitimate save while a forged cross-tenant id can never attach.)

- [ ] **Step 4: Controller passes it**

In `AddRecommendation`, pass `request.ContextSnapshotId` as the new `contextSnapshotId` argument.

- [ ] **Step 5: Run green + commit**

Run the filter + `dotnet build`. Then:
```bash
git add -A
git commit -m "feat(ai-context): link verified context snapshot to saved recommendation"
```

---

## Task 4: Audit retrieval endpoint

**Files:**
- Modify: `src/PatchHound.Api/Models/Decisions/RemediationDecisionDto.cs` (`AnalystRecommendationDto` + new snapshot DTO)
- Modify: `src/PatchHound.Api/Controllers/RemediationDecisionsController.cs`
- Modify: the mapper/query that builds `AnalystRecommendationDto` (find it: `grep -rn "new AnalystRecommendationDto" src/PatchHound.Api`)
- Test: `tests/PatchHound.Tests/Api/RemediationDecisionsControllerTests.cs`

- [ ] **Step 1: Expose `ContextSnapshotId` on the read DTO**

Add trailing `Guid? ContextSnapshotId` to `AnalystRecommendationDto` and populate it wherever the DTO is constructed.

- [ ] **Step 2: Add a snapshot response DTO**

```csharp
public record RecommendationContextSnapshotDto(
    Guid Id,
    Guid RemediationCaseId,
    DateTimeOffset GeneratedAt,
    OperationalContextPack Context,
    IReadOnlyList<OperationalContextCitation> Citations);
```
(Add `using PatchHound.Core.Models.OperationalContext;`.)

- [ ] **Step 3: Write the failing controller test**

Test `GetRecommendationContext_returns_snapshot_for_tenant`: seed a snapshot, call the new endpoint, assert 200 + the pack/citations deserialize. Test `GetRecommendationContext_returns_404_for_unknown`: missing id → 404. Mirror the controller test harness already in the file.

- [ ] **Step 4: Implement the endpoint**

Add to `RemediationDecisionsController` (tenant-scoped; auth = same policy used to view recommendations, e.g. the read policy on the `[HttpGet("recommendations")]` action — match it):

```csharp
    [HttpGet("recommendations/context/{snapshotId:guid}")]
    public async Task<ActionResult<RecommendationContextSnapshotDto>> GetRecommendationContext(
        Guid caseId, Guid snapshotId, CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });

        var snapshot = await dbContext.RecommendationContextSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == snapshotId
                && s.TenantId == tenantId
                && s.RemediationCaseId == caseId, ct);
        if (snapshot is null)
            return NotFound(new ProblemDetails { Title = "Context snapshot not found." });

        var pack = JsonSerializer.Deserialize<OperationalContextPack>(
            snapshot.ContextJson, OperationalContextPack.SerializerOptions)!;
        var citations = JsonSerializer.Deserialize<List<OperationalContextCitation>>(
            snapshot.CitationsJson, OperationalContextPack.SerializerOptions) ?? [];

        return Ok(new RecommendationContextSnapshotDto(
            snapshot.Id, snapshot.RemediationCaseId, snapshot.GeneratedAt, pack, citations));
    }
```

(The controller already injects `tenantContext`; confirm it has a `PatchHoundDbContext dbContext` field — if not, inject it. Add `using` for `System.Text.Json`, the pack models, and EF.)

- [ ] **Step 5: Run green + commit**

```bash
git add -A
git commit -m "feat(ai-context): expose recommendation context snapshot retrieval endpoint"
```

---

## Task 5: Frontend — carry the snapshot id through save

**Files:**
- Modify: `frontend/src/api/remediation.schemas.ts`, `frontend/src/api/remediation.functions.ts`
- Modify: `frontend/src/components/features/remediation/SecurityAnalystWorkbench.tsx`

- [ ] **Step 1: Schema fields**

In `remediation.schemas.ts`: add optional `contextSnapshotId: z.string().uuid().nullish()` to `aiRecommendationDraftSchema`, and to the add-recommendation request schema (find it — the body for `addRecommendation`). Mirror existing optional-field style.

- [ ] **Step 2: Carry the id in the workbench**

In `SecurityAnalystWorkbench.tsx`: extend `aiDraftMeta` (or add a sibling state) to hold `contextSnapshotId` from the draft response in `handleApplyAiRecommendation`. In the save handler that calls `addRecommendation`, include `contextSnapshotId: aiDraftMeta?.contextSnapshotId ?? undefined` in the request body. (Reset it to null when starting a new generate / on error, like the other draft meta.) Do not change unrelated save behavior.

- [ ] **Step 3: Typecheck + lint + test + commit**

Run: `cd frontend && npm run typecheck && npm run lint && npm test`. Then:
```bash
git add -A
git commit -m "feat(ai-context): carry context snapshot id through recommendation save"
```

---

## Task 6: Final verification

- [ ] **Step 1: Backend full suite** — `dotnet test PatchHound.slnx -v minimal` → green.
- [ ] **Step 2: Migration sanity** — `dotnet ef migrations has-pending-model-changes ...` → "No changes".
- [ ] **Step 3: Frontend gates** — `cd frontend && npm run typecheck && npm run lint && npm test` → pass.
- [ ] **Step 4: Tenant-safety reasoning** — confirm: snapshot rows carry `TenantId`; the retrieval endpoint and the link-on-save both filter by `tenantId`; a snapshot from another tenant/case can never be linked or read. The snapshot stores the already-redacted pack (the service redacts in `Finalize` before returning `PackJson`), so no un-redacted data is persisted.

---

## Self-Review (completed during planning)

**Spec coverage:**
- "Exact context snapshot used for a generation is available to an auditor later" → Tasks 1–4 (persist at generation, link on save, retrieve via endpoint). ✅
- Tenant isolation → snapshot carries `TenantId` + query filter; link + retrieval verify tenant+case. ✅
- Stores the post-redaction pack (no un-redacted PII persisted) → the pack JSON is already redacted by `Finalize`. ✅

**Design decision (chosen):** capture-at-generation + link-on-save, server-authoritative (the snapshot is written server-side from the deterministic pack, never from client input), so the audit record can't be tampered with. The save only *links* a pre-existing, ownership-verified snapshot id.

**Known follow-ups (out of scope):** orphan-snapshot cleanup (discarded drafts leave unlinked snapshots — a periodic delete of unlinked rows older than the spec's 90-day window); a frontend "view recorded context" panel on saved recommendations (backend GET endpoint already exposes it); the same persistence for `AIReport`/other generators.

**Type consistency:** `RecommendationContextSnapshot.Create`, `AnalystRecommendation.ContextSnapshotId` (threaded through `Create`+`Update`), `AddRecommendationForCaseAsync(..., contextSnapshotId)`, the DTO fields, and `RecommendationContextSnapshotDto` reuse the Phase-1 `OperationalContextPack`/`OperationalContextCitation` and the existing `OperationalContextPack.SerializerOptions`.

**Placeholder scan:** Task 1 Step 4 (query-filter/FK wiring) and Task 4 Step 1/Step 4 (DTO construction sites, dbContext field on the controller) reference "match the existing pattern / verify the construction site" rather than reproducing unseen code — deliberate, since those must follow the real DbContext filter registration and the real DTO mapper. All new entity/endpoint/service code is complete.
