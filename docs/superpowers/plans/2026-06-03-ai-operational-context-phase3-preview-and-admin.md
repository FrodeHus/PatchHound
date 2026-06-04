# AI Operational Context — Phase 3: On-Demand Preview + Admin Controls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose tenant operational-context controls through the AI-profile API + admin UI, and add on-demand, tenant-scoped preview endpoints that build a context pack fresh under the caller's tenant context and return it (no model call, no shared-row persistence) so admins/analysts can inspect exactly what local data would be supplied.

**Architecture:** Phase 1 built `IAiOperationalContextService` (tenant-scoped, explicit `TenantId` filter) and the profile fields. This phase surfaces them. The preview endpoints are inherently tenant-isolated: the controller reads `ITenantContext.CurrentTenantId` (never a client-supplied tenant) and passes it to the service, which already filters by that tenant. Options come from the tenant's default AI profile (or safe defaults if none). No data is persisted — this is the on-demand replacement for the (removed) leaky persistence on the global `VulnerabilityPatchAssessment`.

**Tech Stack:** ASP.NET Core controllers (`PatchHound.Api`), EF Core, xunit + FluentAssertions; React 19 + TanStack Start/Router + Radix + Tailwind (`frontend/`). Spec: `docs/superpowers/specs/2026-06-01-local-ai-operational-context-rag-design.md` (Phase 3 / Admin + Preview).

**Scope note:** This branch also carries the prerequisite security fix (removal of `ContextJson`/`ContextHash` from the global `VulnerabilityPatchAssessment`), already committed. This plan adds: profile-settings API exposure, the two preview endpoints, and the admin-UI operational-context section. **Out of scope (Plan 4):** model-calling generation (grounded case summary / approval rationale / closure / assignment recommendation persisted to the tenant-scoped `AIReport`/`AnalystRecommendation`) and citation-array validation of free-form output.

---

## File Structure

**Modify (Api):**
- `src/PatchHound.Api/Models/Settings/TenantAiProfileDto.cs` — add 5 op-context fields to `TenantAiProfileDto` + `SaveTenantAiProfileRequest`.
- `src/PatchHound.Api/Controllers/TenantAiProfilesController.cs` — pass the 5 fields through `Create`, all `Update` call sites, `MapDto`, and validation.

**Create (Api):**
- `src/PatchHound.Api/Models/Ai/OperationalContextPreviewDto.cs` — preview response DTO.
- `src/PatchHound.Api/Controllers/AiOperationalContextController.cs` — two GET preview endpoints.

**Create (Tests):**
- `tests/PatchHound.Tests/Api/AiOperationalContextControllerTests.cs`
- extend `tests/PatchHound.Tests/Api/TenantAiProfilesControllerTests.cs` (if it exists; else create) for the settings round-trip.

**Modify (Frontend):**
- `frontend/src/api/ai-settings.functions.ts` — extend the profile/save TS types with the 5 fields; add `previewOperationalContext` server fn.
- `frontend/src/components/features/settings/TenantAiSettingsPage.tsx` — defaults, profile→draft mapping, and a new "Operational context" section.

---

## Task 1: Profile settings API exposure

**Files:**
- Modify: `src/PatchHound.Api/Models/Settings/TenantAiProfileDto.cs`
- Modify: `src/PatchHound.Api/Controllers/TenantAiProfilesController.cs`
- Test: `tests/PatchHound.Tests/Api/TenantAiProfilesControllerTests.cs`

- [ ] **Step 1: Add fields to both DTOs**

In `TenantAiProfileDto.cs`, append to `TenantAiProfileDto` (after `ResponseFormat`):

```csharp
    ,
    bool AllowOperationalContext,
    string OperationalContextMode,
    int MaxOperationalContextTokens,
    bool IncludeDeviceNamesInContext,
    bool IncludeUserNamesInContext
```

And to `SaveTenantAiProfileRequest` (after `ResearchSourceKey = ""`, as defaulted trailing params so existing callers/tests still compile):

```csharp
    ,
    bool AllowOperationalContext = false,
    string OperationalContextMode = "StructuredOnly",
    int MaxOperationalContextTokens = 3000,
    bool IncludeDeviceNamesInContext = true,
    bool IncludeUserNamesInContext = false
```

- [ ] **Step 2: Write the failing controller test**

Read the existing `TenantAiProfilesControllerTests.cs` to match its construction/harness. Add a test that creates a profile via the controller with `AllowOperationalContext = true, MaxOperationalContextTokens = 1500, IncludeDeviceNamesInContext = false`, then lists/gets it and asserts those values round-trip through `MapDto`. If no controller test file exists, create one mirroring another controller test's WebApplicationFactory/DbContext harness in `tests/PatchHound.Tests/Api/`.

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~TenantAiProfilesControllerTests" -v minimal`
Expected: FAIL (values not persisted/projected).

- [ ] **Step 3: Parse + thread the mode enum and wire Create**

Add a helper near the other parse helpers in the controller:

```csharp
    private static OperationalContextMode ResolveOperationalContextMode(string? value) =>
        Enum.TryParse<OperationalContextMode>(value, ignoreCase: true, out var mode)
            ? mode
            : OperationalContextMode.StructuredOnly;
```

In the `TenantAiProfile.Create(...)` call, append the new named args before the closing paren:

```csharp
            researchSourceKey: ResolveResearchSourceKey(request),
            allowOperationalContext: request.AllowOperationalContext,
            operationalContextMode: ResolveOperationalContextMode(request.OperationalContextMode),
            maxOperationalContextTokens: request.MaxOperationalContextTokens,
            includeDeviceNamesInContext: request.IncludeDeviceNamesInContext,
            includeUserNamesInContext: request.IncludeUserNamesInContext
```

- [ ] **Step 4: Thread through every `Update(...)` call site**

There are multiple `profile.Update(...)` calls in this controller (the main update endpoint plus set-default / clear-default / make-default round-trips). For the **request-driven update** endpoint, pass the request values:

```csharp
                request.AllowOperationalContext,
                ResolveOperationalContextMode(request.OperationalContextMode),
                request.MaxOperationalContextTokens,
                request.IncludeDeviceNamesInContext,
                request.IncludeUserNamesInContext
```

For the **preservation round-trips** (set-default, clear-default, make-default — the ones that currently pass `profile.<field>` to keep existing state), pass the existing entity values so settings are preserved:

```csharp
                profile.AllowOperationalContext,
                profile.OperationalContextMode,
                profile.MaxOperationalContextTokens,
                profile.IncludeDeviceNamesInContext,
                profile.IncludeUserNamesInContext
```

(`Update`'s op-context params are required/positional — these were made required in Phase 1. Add the args as the final positional arguments at each call site.)

- [ ] **Step 5: Project in `MapDto`**

In the `MapDto` method, append the new values in the same order as the `TenantAiProfileDto` record:

```csharp
            profile.ResponseFormat.ToString(),
            profile.AllowOperationalContext,
            profile.OperationalContextMode.ToString(),
            profile.MaxOperationalContextTokens,
            profile.IncludeDeviceNamesInContext,
            profile.IncludeUserNamesInContext
```

(Match the exact existing trailing argument of `MapDto`; the five new values go last.)

- [ ] **Step 6: Validate the token budget**

In `ValidateRequest`, add a guard: `MaxOperationalContextTokens` must be between 100 and 32000 (sane prompt budget). Return a `ProblemDetails` titled "MaxOperationalContextTokens must be between 100 and 32000." when out of range.

- [ ] **Step 7: Run green + build**

Run: `dotnet build PatchHound.slnx -v minimal` then `dotnet test PatchHound.slnx --filter "FullyQualifiedName~TenantAiProfilesControllerTests" -v minimal`
Expected: PASS. Fix any other existing controller test that constructs the DTO positionally.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(ai-context): expose operational-context settings through AI profile API"
```

---

## Task 2: On-demand tenant-scoped preview endpoints

**Files:**
- Create: `src/PatchHound.Api/Models/Ai/OperationalContextPreviewDto.cs`
- Create: `src/PatchHound.Api/Controllers/AiOperationalContextController.cs`
- Test: `tests/PatchHound.Tests/Api/AiOperationalContextControllerTests.cs`

- [ ] **Step 1: Create the response DTO**

```csharp
using PatchHound.Core.Models.OperationalContext;

namespace PatchHound.Api.Models.Ai;

public record OperationalContextPreviewDto(
    string ContextKind,
    Guid SubjectId,
    int TokenEstimate,
    bool Truncated,
    DateTimeOffset GeneratedAt,
    OperationalContextPack Context,
    IReadOnlyList<OperationalContextCitation> Citations);
```

- [ ] **Step 2: Write the failing controller tests**

Create `tests/PatchHound.Tests/Api/AiOperationalContextControllerTests.cs`. Mirror the harness another API controller test uses (WebApplicationFactory or direct controller instantiation with a faked `IAiOperationalContextService` + a stub `ITenantContext`). Cover:
- `Preview_remediation_case_returns_pack_for_current_tenant` — fakes `IAiOperationalContextService.BuildForRemediationCaseAsync(currentTenantId, caseId, ...)` returning a known result; asserts 200 + DTO maps `ContextKind`, `TokenEstimate`, citations.
- `Preview_uses_tenant_context_not_client_input` — asserts the service is called with `ITenantContext.CurrentTenantId` (NOT any route/body tenant), via `Arg.Is<Guid>(t => t == currentTenantId)`.
- `Preview_returns_bad_request_when_no_active_tenant` — `CurrentTenantId` null → 400.

Run: `dotnet test PatchHound.slnx --filter "FullyQualifiedName~AiOperationalContextControllerTests" -v minimal`
Expected: FAIL — controller missing.

- [ ] **Step 3: Implement the controller**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatchHound.Api.Auth;
using PatchHound.Api.Models.Ai;
using PatchHound.Core.Enums;
using PatchHound.Core.Interfaces;
using PatchHound.Core.Models.OperationalContext;
using PatchHound.Infrastructure.Data;

namespace PatchHound.Api.Controllers;

[ApiController]
[Route("api/ai/context")]
[Authorize(Policy = Policies.ViewVulnerabilities)]
public class AiOperationalContextController(
    IAiOperationalContextService contextService,
    ITenantContext tenantContext,
    PatchHoundDbContext dbContext) : ControllerBase
{
    [HttpGet("remediation-cases/{caseId:guid}")]
    public async Task<ActionResult<OperationalContextPreviewDto>> PreviewRemediationCase(
        Guid caseId, CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var options = await ResolveOptionsAsync(tenantId, ct);
        var result = await contextService.BuildForRemediationCaseAsync(tenantId, caseId, options, ct);
        return Ok(ToDto(result, caseId));
    }

    [HttpGet("vulnerabilities/{vulnerabilityId:guid}")]
    public async Task<ActionResult<OperationalContextPreviewDto>> PreviewVulnerability(
        Guid vulnerabilityId, CancellationToken ct)
    {
        if (tenantContext.CurrentTenantId is not Guid tenantId)
        {
            return BadRequest(new ProblemDetails { Title = "No active tenant is selected." });
        }

        var options = await ResolveOptionsAsync(tenantId, ct);
        var result = await contextService.BuildForVulnerabilityAsync(tenantId, vulnerabilityId, options, ct);
        return Ok(ToDto(result, vulnerabilityId));
    }

    private async Task<AiOperationalContextOptions> ResolveOptionsAsync(Guid tenantId, CancellationToken ct)
    {
        var profile = await dbContext.TenantAiProfiles.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.IsDefault)
            .Select(p => new
            {
                p.ProviderType,
                p.MaxOperationalContextTokens,
                p.IncludeDeviceNamesInContext,
                p.IncludeUserNamesInContext,
            })
            .FirstOrDefaultAsync(ct);

        return new AiOperationalContextOptions
        {
            MaxTokens = profile?.MaxOperationalContextTokens ?? 3000,
            ProviderIsExternal = profile?.ProviderType.IsExternal() ?? false,
            IncludeDeviceNames = profile?.IncludeDeviceNamesInContext ?? true,
            IncludeUserNames = profile?.IncludeUserNamesInContext ?? false,
        };
    }

    private static OperationalContextPreviewDto ToDto(AiOperationalContextResult result, Guid subjectId) =>
        new(
            result.Pack.ContextKind,
            subjectId,
            result.TokenEstimate,
            result.Truncated,
            DateTimeOffset.UtcNow,
            result.Pack,
            result.Citations);
}
```

- [ ] **Step 4: Run green + build**

Run: `dotnet build PatchHound.slnx -v minimal` then `dotnet test PatchHound.slnx --filter "FullyQualifiedName~AiOperationalContextControllerTests" -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(ai-context): add on-demand tenant-scoped operational context preview endpoints"
```

---

## Task 3: Frontend types + preview API function

**Files:**
- Modify: `frontend/src/api/ai-settings.functions.ts`

- [ ] **Step 1: Extend the profile TS types**

In the file(s) defining the `SaveTenantAiProfile` and the fetched profile types (find with `grep -rn "webResearchMode" frontend/src --include=*.ts --include=*.tsx` — they live next to the existing research fields), add the five fields to BOTH the saved-shape and the fetched-shape types:

```ts
  allowOperationalContext: boolean
  operationalContextMode: 'Disabled' | 'StructuredOnly' | 'StructuredAndSemantic'
  maxOperationalContextTokens: number
  includeDeviceNamesInContext: boolean
  includeUserNamesInContext: boolean
```

- [ ] **Step 2: Add the preview server function**

Mirror the existing `createServerFn` calls in `ai-settings.functions.ts`. Add:

```ts
export const previewVulnerabilityContext = createServerFn({ method: 'GET' })
  .validator((d: { vulnerabilityId: string }) => d)
  .handler(async ({ data }) =>
    apiGet(`/api/ai/context/vulnerabilities/${data.vulnerabilityId}`),
  )

export const previewRemediationCaseContext = createServerFn({ method: 'GET' })
  .validator((d: { caseId: string }) => d)
  .handler(async ({ data }) =>
    apiGet(`/api/ai/context/remediation-cases/${data.caseId}`),
  )
```

(Match the exact request/auth helper the other functions in this file use — e.g. the existing `apiGet`/fetch wrapper and server-fn signature; do not invent a new HTTP helper.)

- [ ] **Step 3: Typecheck**

Run: `cd frontend && npm run typecheck`
Expected: passes (no consumers yet of the new types beyond the additions).

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(ai-context): frontend types + preview API for operational context"
```

---

## Task 4: Admin UI — Operational context section

**Files:**
- Modify: `frontend/src/components/features/settings/TenantAiSettingsPage.tsx`

Mirror the EXISTING research-settings section in this file (the `allowExternalResearch` Checkbox + `webResearchMode` select + `maxResearchSources` input around lines 943–1083). Reuse the same `Checkbox`, select, label, and `onDraftChange((current) => ({ ...current, ... }))` patterns and Tailwind classes already in the file — do not introduce new UI primitives or styles.

- [ ] **Step 1: Defaults in `createEmptyProfile`**

Where the empty draft is built (near line 128 with `allowExternalResearch: false`), add:

```ts
    allowOperationalContext: false,
    operationalContextMode: 'StructuredOnly',
    maxOperationalContextTokens: 3000,
    includeDeviceNamesInContext: true,
    includeUserNamesInContext: false,
```

- [ ] **Step 2: Map fetched profile → draft**

Where the profile is mapped to the draft (near line 157 with `allowExternalResearch: profile.allowExternalResearch`), add:

```ts
    allowOperationalContext: profile.allowOperationalContext,
    operationalContextMode: profile.operationalContextMode,
    maxOperationalContextTokens: profile.maxOperationalContextTokens,
    includeDeviceNamesInContext: profile.includeDeviceNamesInContext,
    includeUserNamesInContext: profile.includeUserNamesInContext,
```

- [ ] **Step 3: Render the section**

Add a new card/section after the research-settings block, mirroring its markup. Include:
- An "Enable operational context" `Checkbox` bound to `draft.allowOperationalContext`.
- When enabled: a mode `<select>` (Disabled / StructuredOnly / StructuredAndSemantic — note in helper text that StructuredAndSemantic behaves as StructuredOnly until embeddings exist), a number input for `maxOperationalContextTokens`, and two checkboxes for `includeDeviceNamesInContext` and `includeUserNamesInContext`.
- Helper text on `includeDeviceNamesInContext`: "External providers redact device names unless enabled."

Each control updates the draft via the same `onDraftChange((current) => ({ ...current, <field>: <value> }))` pattern used by the research fields. Use a representative control mirroring the existing `maxResearchSources` input for the number field and the existing `includeCitations` checkbox for the booleans.

- [ ] **Step 4: Typecheck + lint + test**

Run: `cd frontend && npm run typecheck && npm run lint && npm test`
Expected: all pass. If a snapshot/RTL test asserts the settings form shape, update it for the new section.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(ai-context): add operational context section to AI profile admin UI"
```

---

## Task 5: Final verification

- [ ] **Step 1: Backend full suite**

Run: `dotnet test PatchHound.slnx -v minimal`
Expected: all green (Docker required for Testcontainers).

- [ ] **Step 2: Migration sanity**

Run: `dotnet ef migrations has-pending-model-changes --project src/PatchHound.Infrastructure --startup-project src/PatchHound.Api`
Expected: "No changes" — this phase adds no schema.

- [ ] **Step 3: Frontend gates**

Run: `cd frontend && npm run typecheck && npm run lint && npm test`
Expected: all pass.

- [ ] **Step 4: Manual tenant-isolation reasoning check**

Confirm by reading `AiOperationalContextController` that neither endpoint accepts a tenant id from the route/body/query — the tenant is always `tenantContext.CurrentTenantId`, and the service applies its explicit `TenantId` filter. There is no code path where one tenant can preview another tenant's context.

---

## Self-Review (completed during planning)

**Spec coverage (Phase 3 / Admin + Preview):**
- Admin AI profile operational-context settings (enable, mode, token budget, data-minimization toggles) → Tasks 1, 4. ✅
- Preview endpoint to inspect context before enabling → Task 2 (remediation case) + Task 2 (vulnerability). ✅
- Tenant access + analyst authorization on preview → Task 2 (`[Authorize(Policy = ViewVulnerabilities)]` + `CurrentTenantId`). ✅
- Profile-level redaction controls for external providers → exposed via Task 1 (`IncludeDeviceNamesInContext`/`IncludeUserNamesInContext`), enforced by the Phase-1 redactor. ✅
- On-demand, tenant-scoped, never-persisted (the corrected vulnerability-path design) → Task 2. ✅

**Deferred (intentional, noted in Scope):** model-calling generation of grounded case summary / approval rationale / closure / assignment recommendation persisted to the tenant-scoped `AIReport`/`AnalystRecommendation`, and citation-array validation of free-form model output — Plan 4. The "View cited facts" dialog on the remediation-case detail is part of that generation surface and is deferred with it; this phase ships the admin preview only.

**Type/contract consistency:** `OperationalContextPreviewDto` reuses Phase-1 `OperationalContextPack`/`OperationalContextCitation`; `AiOperationalContextOptions` field names match Phase 1; the 5 profile fields use identical names across entity, DTO, controller, and TS types.

**Placeholder scan:** Frontend tasks reference "match the existing file's pattern" for the TS type location and the UI markup rather than reproducing the 1263-line component — deliberate, since the existing research-settings section is the exact template and reproducing it verbatim would be error-prone. Backend tasks contain complete code.
