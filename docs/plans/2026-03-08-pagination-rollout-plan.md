# Pagination Rollout Plan

## Recommendation

For PatchHound's main data views, use numbered page navigation with a page-size selector.

Why:
- stable and predictable for admin workflows
- works well with filters, bulk actions, and detail drill-down
- easier to test and reason about than infinite scroll
- better state restoration than `Load more`

Use `Load more` only for secondary activity feeds or timelines. Avoid infinite scroll in primary list views.

## Backend Contract

Standardize list responses on:
- `items`
- `totalCount`
- `page`
- `pageSize`
- `totalPages`

Standardize list queries on:
- `page`
- `pageSize`
- existing filters/search/sort

Rules:
- default page size `25`
- allowed UI sizes `25`, `50`, `100`
- hard max page size `100`
- deterministic ordering before `Skip/Take`

## UI Rollout

1. Add one shared pagination control with:
   - previous/next
   - current page
   - page size selector
   - `x-y of total` summary
2. Update route-backed list pages to keep paging in route state where practical.
3. Reset to page `1` when filters change.
4. Keep current page when opening detail panes or dialogs.

## Priority Order

1. Assets
2. Vulnerabilities
3. Remediation Tasks
4. Audit Log
5. Tenants
6. Users
7. Assignment Groups
8. Security Profiles

## Testing

Backend:
- page boundaries
- total count
- deterministic ordering
- page-size clamping

Frontend:
- page changes fetch new data
- page-size changes reset page to `1`
- filter changes reset page to `1`
- empty states still render correctly
