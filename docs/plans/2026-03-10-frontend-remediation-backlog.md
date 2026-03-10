# Frontend Remediation Backlog

**Date:** 2026-03-10
**Status:** Proposed
**Scope:** `frontend/`

## Goal

Turn the frontend review into a concrete execution backlog that improves:

- maintainability
- tenant-scope correctness
- security posture at the frontend/server-function boundary
- deduplication and reuse
- consistency of forms, lists, and detail views
- testing and regression guardrails

This plan is intentionally organized as a delivery sequence, not just a list of observations.

## Review summary

The main frontend risks are structural:

- tenant scope is represented in too many ways
- route files duplicate loader and query orchestration
- large feature files combine container logic and presentation
- list/table features reuse shared chrome, but not shared behavior
- formatting, status mapping, and audit helpers are duplicated
- frontend tests are effectively absent

The existing strengths to preserve:

- validated server functions with Zod
- coherent shared UI primitive layer
- strong visual direction across feature screens
- reasonable auth/session baseline

## Delivery strategy

Ship this in five remediation streams:

1. trust boundary and tenant-scope cleanup
2. route/query architecture cleanup
3. component decomposition and feature boundaries
4. shared behavior and presentation utility extraction
5. tests and guardrails

Recommended rollout:

1. high-risk correctness first
2. repeated architecture second
3. large-component decomposition third
4. shared utility extraction fourth
5. consistency and tests throughout, with guardrails landing early where possible

## Priority buckets

### P0: Correctness and trust boundary

- establish one authoritative tenant-scope contract for server functions
- remove unnecessary `tenantId` inputs from client-callable functions
- standardize frontend handling of `401`, `403`, validation failures, and generic server errors

### P1: Architecture hotspots

- extract shared list-route/query state patterns
- add feature query-key factories
- centralize mutation invalidation per feature
- reduce route-level duplication

### P2: Maintainability hotspots

- split oversized feature components
- move formatting and view-model shaping out of render-heavy files
- remove duplicate helpers from feature files

### P3: Consistency and guardrails

- standardize form controls on shared primitives
- improve accessibility consistency
- add frontend tests around critical route-state and mutation behavior
- add coding rules to prevent fallback into duplicated patterns

## Stream 1: Trust Boundary And Tenant Scope

### Goal

Make frontend tenant handling explicit, predictable, and hard to misuse.

### Problems to fix

- session tenant, selected tenant, and request `tenantId` are all currently in play
- some server functions accept `tenantId` even when scope should come from the active session/scope context
- the contract is ambiguous for future contributors

### Target state

- one frontend concept of selected tenant for user scope
- one server-side mechanism for attaching the active tenant to backend requests
- `localStorage` remains a UI preference only
- server-function inputs do not accept tenant identifiers unless the action is explicitly cross-tenant admin behavior

### Tasks

#### 1.1 Define tenant-scope contract

Document and implement:

- which routes are tenant-scoped
- how the active tenant is selected
- how server functions receive the selected tenant
- which admin actions are exempt and may specify a tenant explicitly

#### 1.2 Introduce a single scope adapter

Add a small server-side adapter used by tenant-scoped server functions:

- reads authenticated session
- reads active UI-selected tenant from one approved source
- validates the selected tenant against the user’s accessible tenants
- attaches the effective tenant to backend API requests

#### 1.3 Remove free-form tenant inputs

Audit server functions and remove `tenantId` from inputs where it should not be user-supplied, especially:

- security profiles
- software browsing/detail
- tenant-scoped lists and detail APIs

Keep explicit tenant input only where the screen is intentionally global-admin scoped.

#### 1.4 Add typed API error translation

Introduce typed frontend/server-function errors:

- `UnauthenticatedError`
- `ForbiddenError`
- `ValidationError`
- `ApiRequestError`

Use these in a shared API boundary utility instead of throwing plain `Error(...)`.

### Acceptance criteria

- tenant-scoped server functions no longer accept `tenantId` unless required
- active tenant handling is documented and enforced in one place
- pages do not need to manually combine tenant UI state and server-function inputs

## Stream 2: Route And Query Architecture

### Goal

Stop duplicating list-route orchestration and cache behavior in every route file.

### Problems to fix

- loaders and `useQuery` rebuild the same request objects
- query keys are embedded as string arrays throughout the app
- `navigate({ search })` handlers are repeated field by field
- mutation success handling uses ad hoc `invalidate()` and `refetch()`

### Target state

- feature query keys come from one module per feature
- list routes use shared request-building patterns
- route search updates follow a consistent helper pattern
- mutation success invalidates known feature queries, not the whole route tree by default

### Tasks

#### 2.1 Add feature query-key factories

Create modules such as:

- `frontend/src/features/assets/query-keys.ts`
- `frontend/src/features/vulnerabilities/query-keys.ts`
- `frontend/src/features/software/query-keys.ts`
- `frontend/src/features/tasks/query-keys.ts`
- `frontend/src/features/admin/query-keys.ts`

Use them for:

- list queries
- detail queries
- related subqueries
- invalidation targets

#### 2.2 Extract list request builders

For each list-heavy feature, add request-building helpers that both loader and client query use.

Examples:

- `buildAssetsListRequest(search)`
- `buildVulnerabilitiesListRequest(search)`
- `buildSoftwareListRequest(search)`
- `buildTasksListRequest(search)`

#### 2.3 Extract search-state helpers

Introduce small route helpers for common patterns:

- update one filter and reset to page 1
- clear filters to defaults
- update page
- update page size and reset page

These should remove repetitive inline `navigate({ search: ... })` lambdas.

#### 2.4 Standardize feature invalidation helpers

Examples:

- asset mutation invalidates asset list and selected asset detail
- vulnerability comment mutation invalidates vulnerability comments only
- task status update invalidates task list
- security-profile creation invalidates security-profile list and dependent pages where needed

### Acceptance criteria

- route files no longer duplicate loader and query request shaping
- cache invalidation uses feature-owned helpers or key factories
- list routes become noticeably smaller and more uniform

## Stream 3: Component Decomposition

### Goal

Reduce oversized components into maintainable feature modules with clear boundaries.

### Initial hotspots

- `components/features/assets/AssetDetailPane.tsx`
- `components/features/vulnerabilities/VulnerabilityDetail.tsx`
- `components/features/admin/TenantSourceManagement.tsx`
- `routes/_authed/admin/security-profiles.tsx`

### Target state

- route or container handles data and interaction orchestration
- section components receive shaped data and callbacks
- formatting and derivation logic live outside large render functions

### Tasks

#### 3.1 Split asset detail pane

Suggested slices:

- asset summary header
- ownership section
- device-specific section
- software-specific section
- vulnerability list section
- metadata renderer
- shared display helpers

#### 3.2 Split vulnerability detail

Suggested slices:

- overview header and panels
- sources section
- matched software section
- references section
- tab navigation
- recurrence/history section
- local query/mutation hook for comments/timeline

#### 3.3 Split tenant source management

Suggested slices:

- summary strip
- source list container
- source card
- credential form subsection
- runtime/status subsection
- action bar and status feedback

#### 3.4 Restructure security profiles route

Break the page into:

- page container
- create profile form
- profile list panel
- guidance/help content
- recent audit panel wiring

### Acceptance criteria

- hotspot files are reduced substantially in size
- section components are reusable within feature boundaries
- routes no longer own large volumes of UI logic

## Stream 4: Shared Behavior And Presentation Utilities

### Goal

Promote real reuse where semantics are stable, without forcing generic abstractions too early.

### Problems to fix

- duplicated `startCase`, date formatting, audit parsing, and status formatting
- repeated domain option arrays
- repeated list/table active-filter logic
- repeated panel and metric-card recipes

### Tasks

#### 4.1 Add shared presentation utilities

Suggested files:

- `frontend/src/lib/formatting.ts`
- `frontend/src/lib/dates.ts`
- `frontend/src/lib/audit.ts`

Move in:

- `startCase`
- common date/dateTime formatting
- audit JSON parsing
- audit entity/key formatting

#### 4.2 Centralize domain display options

Suggested files:

- `frontend/src/lib/options/severity.ts`
- `frontend/src/lib/options/tasks.ts`
- `frontend/src/lib/options/security-profiles.ts`

Move in:

- severity options
- task status options
- security-profile environment/internet/requirement options

#### 4.3 Introduce filtered-list helpers

Do not build a generic mega-component.

Instead extract helpers for:

- active filter chip generation
- common empty-state copy patterns
- filter field layout patterns

#### 4.4 Extract repeated layout primitives carefully

Candidates:

- summary stat strip
- section heading/eyebrow
- bordered panel with standard gradient/background recipes

Do not abstract feature-specific cards prematurely.

### Acceptance criteria

- duplicate helper functions are removed from feature files
- domain option lists are shared
- common formatting decisions are made in one place

## Stream 5: Forms, Accessibility, Tests, And Guardrails

### Goal

Raise baseline quality so future feature work does not reintroduce the same issues.

### Problems to fix

- raw controls coexist with shared controls
- frontend tests are missing
- accessibility quality is inconsistent
- style drift is visible in hand-edited files

### Tasks

#### 5.1 Standardize form controls

Replace raw controls with shared primitives where practical:

- raw `<select>` to shared `Select`
- raw checkbox inputs to shared `Checkbox`
- raw buttons and text inputs to shared `Button` and `Input`/`Textarea` patterns

Prioritize:

- task status update
- security profiles form
- role management dialog
- asset/software detail editing affordances

#### 5.2 Add accessibility pass

Review:

- labels and names for controls
- keyboard interaction in dialogs/sheets
- filter toggles and checkbox semantics
- table action accessibility

#### 5.3 Add frontend tests

Start with behavior that is expensive to verify manually:

- list route search-to-request mapping
- mutation invalidation behavior
- tenant-scope selection effects
- detail screen conditional rendering

Suggested first targets:

- assets list route
- vulnerabilities list route
- software detail route
- tenant scope provider

#### 5.4 Add maintainability guardrails

Recommended repo standards:

- feature query keys must live in dedicated modules
- shared formatting helpers must be used instead of local copies
- tenant-scoped server functions may not accept free-form `tenantId` unless documented
- new list routes should use request-builder helpers

Enforce via:

- code review checklist
- lint rules where practical
- short architecture note in `docs/plans` or code standards

### Acceptance criteria

- critical flows have frontend tests
- shared form controls are the default, not the exception
- accessibility checks are part of UI change review

## Recommended PR sequence

### PR 1: Tenant scope contract cleanup

- define and implement the active tenant contract
- remove unnecessary `tenantId` inputs from tenant-scoped server functions
- add typed API error mapping

### PR 2: Query key factories

- add feature query-key modules
- migrate assets, vulnerabilities, software, tasks

### PR 3: List request builders and search helpers

- extract shared list request builders
- reduce duplication in route loaders and `useQuery`

### PR 4: Asset and vulnerability detail decomposition

- split `AssetDetailPane`
- split `VulnerabilityDetail`

### PR 5: Shared formatting and domain option utilities

- move date formatting, casing, audit helpers, and option arrays

### PR 6: Form control standardization

- migrate raw controls in highest-traffic flows to shared primitives

### PR 7: Frontend tests for route/query state

- add first test suite for route-state mapping and mutation invalidation

### PR 8: Admin workflow decomposition

- split `TenantSourceManagement`
- split `security-profiles` route

## Effort and impact

### Highest impact, lowest regret

- tenant-scope contract cleanup
- query-key factories
- list request builders
- shared formatting/date utilities

### Highest impact, higher effort

- detail-view decomposition
- admin workflow decomposition
- frontend behavioral tests

### Useful but should follow the above

- visual primitive extraction
- broad form-control standardization across all screens

## Risks

### Over-abstraction

Risk:

- creating generic list or table systems that are harder to understand than the duplicated code

Mitigation:

- extract helpers and feature-level patterns first
- do not abstract columns or feature-specific summaries unless repetition is strong and stable

### Tenant-scope regressions

Risk:

- changing tenant propagation without aligning server-function behavior

Mitigation:

- land tenant-scope cleanup first
- add tests around selected tenant behavior

### Large PR churn

Risk:

- mixing architecture cleanup with visual refactors

Mitigation:

- keep PRs narrowly scoped
- separate behavior changes from layout/styling cleanup where possible

## Definition of done

This backlog is complete when:

- tenant scope is unambiguous
- list routes do not duplicate loader/query request shaping
- feature query invalidation is standardized
- large detail screens are decomposed into maintainable modules
- shared formatting and domain display logic are centralized
- critical frontend flows have automated test coverage

