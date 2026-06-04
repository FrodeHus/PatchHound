# AI Operational Context — Phase 5: Remediation-Case Citations + "View Cited Facts" Modal

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make grounded-recommendation citations actually appear, and let analysts drill into them. (1) The remediation-case context builder currently emits **no citations**, so the Phase-4 grounded recommendation draft is always `uncited`; add top-affected-device citations (mirroring the vulnerability builder) via a shared helper. (2) Replace the inline citation chips in the analyst workbench with a "View cited facts" dialog that shows each citation's label + fact + type and links to the relevant entity detail.

**Architecture:** Extract the existing vulnerability-builder top-device citation query into one private `BuildTopDeviceCitationsAsync` helper on `AiOperationalContextService`, and call it from both builders — DRY, and remediation-case packs gain identical `device-risk-top-N` citations. No new data is sent that wasn't already tenant-scoped. The modal is presentation-only over the existing `AiCitationDto` (`key`/`entityType`/`entityId`/`label`/`fact`) already returned by the draft API.

**Tech Stack:** EF Core (`PatchHound.Infrastructure`), xunit + FluentAssertions, Testcontainers; React 19 + TanStack Router (`frontend/`), Radix Dialog. Spec: `docs/superpowers/specs/2026-06-01-local-ai-operational-context-rag-design.md` ("View cited facts" dialog; citation chips that open entity detail). Builds on merged Phases 1–4.

**Scope note:** No API/DTO/schema change — the citation data already flows through `AiRecommendationDraftDto`/`aiRecommendationDraftSchema`. This plan adds remediation-case citations (backend) + the modal (frontend). No migration.

---

## File Structure

**Modify (Infrastructure):**
- `src/PatchHound.Infrastructure/Services/OperationalContext/AiOperationalContextService.cs` — extract `BuildTopDeviceCitationsAsync`; call from both builders; remediation-case pack sets `Citations`.

**Modify (Tests):**
- `tests/PatchHound.Tests/Infrastructure/Services/OperationalContext/AiOperationalContextServiceTests.cs` — assert remediation-case pack has top-device citations + remains tenant-scoped.

**Modify (Frontend):**
- `frontend/src/components/features/remediation/SecurityAnalystWorkbench.tsx` — replace inline chips with a "View cited facts" Dialog (entity links).

---

## Task 1: Remediation-case top-device citations (shared helper)

**Files:**
- Modify: `src/PatchHound.Infrastructure/Services/OperationalContext/AiOperationalContextService.cs`
- Test: `tests/PatchHound.Tests/Infrastructure/Services/OperationalContext/AiOperationalContextServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `AiOperationalContextServiceTests.cs` (reuse the file's existing `CreateService(db)` / `Options()` / `OperationalContextSeed` helpers):

```csharp
    [Fact]
    public async Task RemediationCase_pack_includes_top_device_citations()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateDbContext();
        var seed = await OperationalContextSeed.SeedRemediationCaseAsync(db);
        var service = CreateService(db);

        var result = await service.BuildForRemediationCaseAsync(
            seed.TenantId, seed.RemediationCaseId, Options(), CancellationToken.None);

        result.Pack.Citations.Should().NotBeEmpty();
        result.Pack.Citations.Should().OnlyContain(c => c.EntityType == "Device");
        result.Pack.Citations.Select(c => c.Key).Should().Contain("device-risk-top-1");
    }

    [Fact]
    public async Task RemediationCase_citations_exclude_other_tenant_devices()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateDbContext();
        var seed = await OperationalContextSeed.SeedRemediationCaseAsync(db);
        await OperationalContextSeed.SeedOtherTenantExposureForSameSoftwareAsync(db, seed);
        var service = CreateService(db);

        var result = await service.BuildForRemediationCaseAsync(
            seed.TenantId, seed.RemediationCaseId, Options(), CancellationToken.None);

        // Only the subject tenant's devices (2) — never the other tenant's device.
        result.Pack.Citations.Count.Should().BeLessThanOrEqualTo(2);
    }
```

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~AiOperationalContextServiceTests.RemediationCase_pack_includes_top_device_citations" -v minimal`
Expected: FAIL — remediation-case pack has empty citations.

- [ ] **Step 2: Extract the shared helper**

In `AiOperationalContextService.cs`, add a private helper (place it near `Finalize`). This is the exact logic currently inline in `BuildForVulnerabilityAsync`:

```csharp
    private async Task<List<OperationalContextCitation>> BuildTopDeviceCitationsAsync(
        Guid tenantId,
        IReadOnlyList<Guid> deviceIds,
        AiOperationalContextOptions options,
        CancellationToken ct)
    {
        var topDevices = await db.DeviceRiskScores.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && deviceIds.Contains(s.DeviceId))
            .OrderByDescending(s => s.OverallScore)
            .Take(options.TopDeviceLimit)
            .Join(db.Devices.IgnoreQueryFilters(), s => s.DeviceId, d => d.Id,
                (s, d) => new { d.Id, d.Name, d.Criticality, s.OverallScore })
            .ToListAsync(ct);

        return topDevices.Select((d, i) => new OperationalContextCitation
        {
            Key = $"device-risk-top-{i + 1}",
            EntityType = "Device",
            EntityId = d.Id,
            Label = d.Name,
            Fact = $"Device {d.Name} risk score {d.OverallScore:0}, {d.Criticality} asset",
            RiskWeight = (double)d.OverallScore,
        }).ToList();
    }
```

- [ ] **Step 3: Use the helper in the vulnerability builder**

In `BuildForVulnerabilityAsync`, replace the inline `topDevices` query + `citations` projection (the block currently producing `var citations = topDevices.Select(...)`) with:

```csharp
        var citations = await BuildTopDeviceCitationsAsync(tenantId, deviceIds, options, ct);
```

Leave the pack's `Citations = citations` as-is. Confirm no now-unused `topDevices` local remains.

- [ ] **Step 4: Populate citations in the remediation-case builder**

In `BuildForRemediationCaseAsync`, after `maxDeviceRisk` is computed and before building the pack, add:

```csharp
        var citations = await BuildTopDeviceCitationsAsync(tenantId, deviceIds, options, ct);
```

Then add `Citations = citations,` to the `OperationalContextPack` initializer (e.g. after the `Risk = ...` line).

- [ ] **Step 5: Run green**

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~AiOperationalContextServiceTests" -v minimal`
Expected: PASS (existing isolation/criticality tests + the 2 new citation tests). The vulnerability path's existing `Vulnerability_pack_excludes_other_tenant_for_same_cve` test still passes (helper behavior identical).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(ai-context): emit top-device citations for remediation-case context"
```

---

## Task 2: "View cited facts" modal in the analyst workbench

**Files:**
- Modify: `frontend/src/components/features/remediation/SecurityAnalystWorkbench.tsx`

The grounding block (≈ lines 348–376) currently renders the "Grounded with local context" badge, an uncited caption, or inline chips. Replace the inline chips with a "View cited facts (N)" button that opens a Dialog.

- [ ] **Step 1: Read the Dialog primitive + an existing usage**

Read `frontend/src/components/ui/dialog.tsx` for the exported parts (`Dialog`, `DialogContent`, `DialogHeader`, `DialogTitle`, `DialogTrigger`, etc.), and find one existing modal usage in the codebase (`grep -rln "DialogContent" frontend/src/components/features | head`) to copy the open/close + styling pattern. Use TanStack Router `Link` (already imported elsewhere in the remediation feature) for entity links.

- [ ] **Step 2: Add modal open state**

Add near the other `useState` hooks:

```tsx
  const [citedFactsOpen, setCitedFactsOpen] = useState(false)
```

- [ ] **Step 3: Replace the inline chips with a button + Dialog**

Keep the "Grounded with local context" badge and the `uncited` caption. Replace the `aiDraftMeta.citations.length > 0 ? (<div ...chips>) : null` branch with a "View cited facts ({count})" button (reuse the file's existing `Button` with a subtle variant) that sets `citedFactsOpen(true)`, plus a `Dialog`:

```tsx
{aiDraftMeta.uncited ? (
  <p className="text-xs text-muted-foreground">No local facts were cited.</p>
) : aiDraftMeta.citations.length > 0 ? (
  <>
    <Button
      type="button"
      variant="ghost"
      size="sm"
      onClick={() => setCitedFactsOpen(true)}
    >
      View cited facts ({aiDraftMeta.citations.length})
    </Button>
    <Dialog open={citedFactsOpen} onOpenChange={setCitedFactsOpen}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Cited local facts</DialogTitle>
        </DialogHeader>
        <ul className="space-y-3">
          {aiDraftMeta.citations.map((citation) => (
            <li key={citation.key} className="rounded-lg border border-border/70 p-3">
              <div className="flex items-center justify-between gap-2">
                <span className="text-sm font-medium">{citation.label}</span>
                <span className="rounded-full border px-2 py-0.5 text-xs text-muted-foreground">
                  {citation.entityType}
                </span>
              </div>
              <p className="mt-1 text-xs text-muted-foreground">{citation.fact}</p>
              {citation.entityType === 'Device' ? (
                <Link
                  to="/devices/$id"
                  params={{ id: citation.entityId }}
                  className="mt-2 inline-block text-xs text-primary hover:underline"
                  onClick={() => setCitedFactsOpen(false)}
                >
                  View device
                </Link>
              ) : null}
            </li>
          ))}
        </ul>
      </DialogContent>
    </Dialog>
  </>
) : null}
```

Match the real exported Dialog component names and the real device-detail route path (confirm with `grep -n "devices/\$id" frontend/src/routeTree.gen.ts` or the route file). Use the file's existing className idioms; do not add new UI primitives or deps. Add the `Dialog*` and `Link` imports if not already present.

- [ ] **Step 4: Typecheck + lint + test**

Run: `cd frontend && npm run typecheck && npm run lint && npm test`
Expected: pass. If a workbench test asserts the chip markup, update it for the button/dialog. Consider adding a small RTL test: after the draft is applied with citations, the "View cited facts" button appears and opens the dialog (only if the existing workbench test already mocks the draft generation; otherwise skip to avoid scope creep).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(ai-context): add View cited facts dialog to analyst workbench"
```

---

## Task 3: Final verification

- [ ] **Step 1: Backend full suite** — `dotnet test PatchHound.slnx -v minimal` → all green.
- [ ] **Step 2: Migration sanity** — `dotnet ef migrations has-pending-model-changes --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api` → "No changes".
- [ ] **Step 3: Frontend gates** — `cd frontend && npm run typecheck && npm run lint && npm test` → pass.
- [ ] **Step 4: End-to-end sanity (reasoning)** — confirm the remediation-case builder now emits `device-risk-top-N` citations, so a grounded recommendation draft can return non-empty `citations` / `uncited == false`, and the modal links Device citations to `/devices/$id`.

---

## Self-Review (completed during planning)

**Spec coverage:**
- "View cited facts" dialog → Task 2. ✅
- Citation chips/links that open the relevant entity detail → Task 2 (Device → `/devices/$id`; other entity types render without a link until those citation types exist). ✅
- Makes the Phase-4 citation feature functional (remediation-case path actually emits keys) → Task 1. ✅
- Tenant isolation preserved → Task 1 reuses the explicit-`TenantId`-filtered helper; new test asserts no cross-tenant citation. ✅

**Why Task 1 is needed (gap found during planning):** `BuildForRemediationCaseAsync` never set `Citations`, so the Phase-4 grounded recommendation draft (which uses it) was always `uncited` and a facts modal would always be empty. Task 1 closes that gap by sharing the vulnerability builder's existing, already-tested citation logic.

**Type consistency:** `BuildTopDeviceCitationsAsync` returns `List<OperationalContextCitation>` used identically by both builders; the modal consumes the existing `aiDraftMeta.citations` shape (`key`/`entityType`/`entityId`/`label`/`fact`) — no API/schema change.

**Placeholder scan:** Task 2 references "confirm the real Dialog export names / device route path" rather than guessing the primitive's API — deliberate, since the exact exports must match `dialog.tsx`. Backend code is complete.
