# Frontend Design Review — Tasks

> Review performed 2026-03-20. Issues ordered by impact.

---

## High Priority

### 1. Standardize border opacity scale
**Files:**
- `frontend/src/components/ui/card.tsx`
- `frontend/src/components/ui/data-table.tsx`
- `frontend/src/components/ui/pagination-controls.tsx`
- `frontend/src/components/ui/data-table-workbench.tsx`
- `frontend/src/components/ui/inset-panel.tsx`

**Problem:** Border opacity values (`/60`, `/70`, `/80`) used inconsistently across components — table rows mix `/60` and `/70`, cards use `/80`, headers vary.

**Fix:** Adopt a 3-tier system:
- `/80` — primary borders (cards, panels, inputs)
- `/70` — secondary borders (table headers, section dividers)
- `/60` — tertiary borders (table rows, subtle separators)

Audit all `border-border/` usages and align to the scale.

---

### 2. Standardize background opacity scale
**Files:**
- `frontend/src/components/ui/pagination-controls.tsx` (`/35`)
- `frontend/src/components/ui/data-table.tsx` (`/55`)
- `frontend/src/components/ui/data-table-workbench.tsx` (`/30`)
- Various feature components

**Problem:** Background opacity values range from `/30` to `/80` without clear hierarchy.

**Fix:** Define 4 stops:
- `/30` — very light (pagination, subtle containers)
- `/50` — light (table headers, filter sections)
- `/70` — medium (badges, hover states)
- `/80` — strong (inputs, selects, buttons)

---

### 3. Fix card padding asymmetry
**Files:**
- `frontend/src/components/ui/card.tsx`

**Problem:** `CardContent` has `px-4` but no explicit vertical padding, while `CardFooter` uses symmetric `p-4`. Vertical spacing depends on parent gap which is fragile.

**Fix:** Give `CardContent` explicit vertical padding or document that the parent `Card` gap handles it consistently.

---

### 4. Consolidate border radius values
**Files:**
- `frontend/src/components/features/dashboard/` (multiple files use `rounded-[24px]`, `rounded-[32px]`)
- `frontend/src/app.css` (CSS custom properties)

**Problem:** Semantic Tailwind values (`rounded-lg`, `rounded-xl`, `rounded-2xl`) mixed with hardcoded pixel values (`rounded-[24px]`, `rounded-[32px]`).

**Fix:** Define CSS custom properties for dashboard card radii and use semantic classes everywhere:
```css
--radius-card: 1.5rem;    /* 24px */
--radius-card-lg: 2rem;   /* 32px */
```

---

### 5. Standardize typography tracking
**Files:**
- `frontend/src/components/features/dashboard/SecureScoreCard.tsx`
- `frontend/src/components/features/dashboard/PostureCard.tsx`
- `frontend/src/components/ui/sidebar.tsx`
- Various label elements across feature components

**Problem:** Uppercase labels use `tracking-[0.16em]`, `tracking-[0.18em]`, `tracking-[0.24em]`, `tracking-[0.28em]` inconsistently.

**Fix:** Define 2 standard tracking values:
- `tracking-[0.18em]` — small-caps labels
- `tracking-[-0.04em]` — large display numbers

---

## Medium Priority

### 6. Add toast notification system
**Files:**
- Create: `frontend/src/components/ui/toast.tsx` (or use sonner)
- Modify: All mutation call sites (asset rules, teams, tenants, remediation tasks)

**Problem:** Mutations (delete, toggle, run) have no success/error feedback. Failed mutations silently revert optimistic updates. Users have no confirmation that actions succeeded.

**Fix:** Add a toast provider (e.g., sonner) and surface `onSuccess`/`onError` messages for all mutations. Minimum coverage:
- Delete confirmations: "Rule deleted"
- Toggle actions: "Rule enabled" / "Rule disabled"
- Run actions: "Rules evaluation started"
- Error fallback: "Something went wrong. Please try again."

---

### 7. Add content-aware skeleton loaders
**Files:**
- Create: `frontend/src/components/ui/skeleton.tsx`
- Modify: Route loaders / Suspense boundaries

**Problem:** Loading states use bare `animate-pulse` rectangles with no shape context. Users see grey blocks with no hint of what content is loading.

**Fix:** Create skeleton variants that match content shapes:
- `TableSkeleton` — rows with column-width blocks
- `CardSkeleton` — header + content area
- `StatSkeleton` — large number + label

---

### 8. Standardize icon sizes
**Files:** All components using lucide-react icons

**Problem:** Icons use `size-2.5`, `size-3`, `size-3.5`, `size-4`, `size-5` without clear rules.

**Fix:** Define a 3-tier scale:
- `size-3` (12px) — badges, inline indicators
- `size-4` (16px) — buttons, table actions, nav items
- `size-5` (20px) — page-level icons, empty states

---

### 9. Add dialog size variants
**Files:**
- `frontend/src/components/ui/dialog.tsx`

**Problem:** All dialogs use `sm:max-w-sm` (~384px). Confirmation dialogs are fine at this size, but form-heavy dialogs (asset rule wizard, filter builder) feel cramped.

**Fix:** Add size prop:
- `sm` (384px) — confirmations, simple forms
- `md` (512px) — multi-field forms
- `lg` (640px) — wizards, complex editors

---

### 10. Increase sort indicator visibility
**Files:**
- `frontend/src/components/ui/sortable-column-header.tsx`

**Problem:** The unsorted `ArrowUpDown` icon uses `text-muted-foreground/50` which is nearly invisible. Users may not discover that columns are sortable.

**Fix:** Change unsorted icon opacity from `/50` to `/70` and consider adding a subtle hover effect on the header button.

---

## Low Priority

### 11. Improve empty states
**Files:**
- `frontend/src/components/ui/data-table-empty-state.tsx`
- Individual route pages

**Problem:** Empty tables show generic "No rows found." text. No contextual guidance or calls to action.

**Fix:** Add contextual empty states per table:
- Asset rules: "No rules yet. Create your first rule to automatically classify assets."
- Vulnerabilities: "No vulnerabilities found matching your filters."
- Include a primary action button where applicable.

---

### 12. Add error boundaries
**Files:**
- Create: `frontend/src/components/ui/error-boundary.tsx`
- Modify: `frontend/src/routes/__root.tsx` or layout routes

**Problem:** No visible error recovery UI. A failed API call likely renders a blank page or crashes the component tree.

**Fix:** Add route-level error boundaries with:
- Friendly error message
- "Try again" button that retries the route loader
- Option to navigate home

---

### 13. Add breadcrumb tooltips for truncated text
**Files:**
- `frontend/src/components/ui/breadcrumbs.tsx`

**Problem:** Current page crumb truncates at `max-w-[200px]`. Long entity names (vulnerability titles, rule names) get cut off with no way to see the full text.

**Fix:** Wrap the truncated crumb in a `Tooltip` showing the full text.

---

### 14. Expand tooltip usage
**Files:** Various feature components

**Problem:** Tooltip component exists but is barely used. Icon-only buttons, truncated cell content, and abbreviations lack hover explanations.

**Fix:** Add tooltips to:
- All icon-only action buttons (trash, edit, play icons in tables)
- Truncated cell content (long vulnerability names, descriptions)
- Abbreviated values (severity codes, status labels)

---

### 15. Add "clear all filters" shortcut
**Files:**
- `frontend/src/components/ui/data-table-workbench.tsx`

**Problem:** Active filters can be cleared individually, but there's no single "Clear all" action when multiple filters are active.

**Fix:** Add a "Clear all" link/button next to the active filter badges when 2+ filters are applied.
