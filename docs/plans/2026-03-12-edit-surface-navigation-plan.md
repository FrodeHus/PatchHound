# Edit Surface And Navigation Plan

## Goal

Stop forcing dense admin/settings forms into narrow side sheets.

Use the right edit surface for the task:

- inline for small edits
- sheet for medium-complexity reusable objects
- dedicated edit page for dense, operational, or multi-section configuration

When dedicated edit pages are used, keep navigation task-oriented:

- clear back link
- preserved return context
- predictable save/cancel behavior

## Design Rule

Choose the edit surface by task weight, not by visual consistency alone.

### Keep in sheet

Use a sheet when the edit task:

- has one clear goal
- fits in roughly one viewport without meaningful compression
- does not need side-by-side reasoning
- does not combine credentials, validation, preview, and advanced settings

### Move to dedicated edit page

Use a dedicated page when the edit task:

- has multiple major sections
- needs operational context while editing
- includes validation or testing actions
- includes advanced runtime settings
- includes credentials or secrets
- benefits from a side rail
- becomes visually squashed inside a sheet

## Route-by-Route Recommendation

### Keep sheet-based

#### Security Profiles

Current route:

- `/admin/security-profiles`

Recommendation:

- keep list-first page
- keep create/edit in shared sheet

Reason:

- reusable object
- bounded field count
- no ongoing operational context required during edit
- CVSS workbench now opens in a dialog, so the sheet is no longer overloaded

### Move to dedicated edit pages

#### Tenant Sources

Current route:

- `/admin/sources`

Current edit surface:

- inline list with edit sheet in `TenantSourceManagement`

Recommended split:

- `/admin/sources`
  - overview, source state, manual sync, run history
- `/admin/sources/$sourceKey`
  - dedicated tenant-source edit page

What belongs on the dedicated page:

- connection settings
- credentials
- runtime flags
- sync schedule
- validation/test actions
- recent run status / operational hints

Reason:

- source configuration is operationally dense
- secrets and schedule editing should not be squeezed into a right rail
- the user often needs context while editing

#### AI Profiles

Current route:

- `/settings/ai`

Current edit surface:

- list and shared edit sheet in `TenantAiSettingsPage`

Recommended split:

- `/settings/ai`
  - overview, profile list, default status, validation posture
- `/settings/ai/$id`
  - dedicated profile edit page
- optional:
  - `/settings/ai/new`

What belongs on the dedicated page:

- provider connection fields
- model/runtime controls
- prompt editor
- validation actions
- model discovery
- default profile controls
- provider-specific guidance

Reason:

- AI profile editing is already past the comfortable sheet threshold
- provider config + validation + prompt + runtime controls is a full configuration workspace

### Keep as dedicated page

#### Tenant Administration Detail

Current route:

- `/admin/tenants/$id`

Recommendation:

- keep as dedicated detail page

Reason:

- already reads like a primary detail/config page
- not a candidate for sheet conversion

## Navigation Model For Dedicated Edit Pages

Dedicated edit pages must preserve task continuity.

### Back link

Each edit page should have a visible back action in the header:

- `← Back to Sources`
- `← Back to AI Profiles`

### Return context

Preserve the origin in route search params:

- `?returnTo=/admin/sources`
- `?returnTo=/settings/ai`
- include list state when useful:
  - filters
  - page
  - active tab

Recommended pattern:

- list page builds links with `returnTo`
- edit page reads `returnTo`
- save/cancel navigation uses that value

### Save behavior

For dense configuration pages, use:

- `Save changes`
- `Cancel`

Recommended default behavior:

- save and stay on page if validation or follow-up actions are common
- save and return only when the edit is simple and final

For this repo:

- Sources: save and stay
- AI Profiles: save and stay

Both should also expose:

- `Back to list`

### Cancel behavior

Cancel should:

- return to `returnTo` if present
- otherwise return to the list route

### Success feedback

After save:

- show inline success state on the edit page
- when returning to list, optionally show a toast and highlight the edited row later if needed

## Page Layout Recommendation

### Dedicated edit page layout

Use:

- header with title, status, and back link
- main form column
- optional right rail for validation, history, or guidance
- sticky footer or sticky save bar

Avoid:

- card-inside-card stacks for every field group
- heavy inline helper text under every field
- hiding all complexity behind accordions

### Right rail candidates

#### Sources

- last run
- last success
- last error
- manual sync
- run history link

#### AI Profiles

- validation status
- last validated at
- provider summary
- model discovery
- default profile posture

## Rollout Order

### Phase 1

Move AI profile editing from sheet to dedicated page.

Reason:

- highest complexity
- least appropriate current fit for a sheet
- clean route surface already exists at `/settings/ai`

### Phase 2

Move tenant source editing from sheet to dedicated page.

Reason:

- second-highest complexity
- strong operational context needed while editing

### Phase 3

Evaluate whether any remaining list areas need the same treatment.

Current likely no-change areas:

- security profiles
- simple creation dialogs like assignment groups

## Implementation Notes

### AI routes

Add:

- `/settings/ai/$id`
- optionally `/settings/ai/new`

Refactor:

- keep `TenantAiSettingsPage` as list/overview page
- extract shared form into reusable profile editor component used by dedicated route

### Source routes

Add:

- `/admin/sources/$sourceKey`

Refactor:

- keep `TenantSourceManagement` as overview/list surface
- extract shared source editor into reusable page component

## Recommendation Summary

- keep security profiles sheet-based
- move AI profile editing to dedicated edit pages
- move tenant source editing to dedicated edit pages
- always preserve `returnTo`
- always show a clear back link
- prefer save-and-stay for dense operational edit pages
