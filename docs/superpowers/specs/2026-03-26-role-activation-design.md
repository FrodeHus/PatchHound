# Role Activation Design

## Overview

Users log in with a baseline "User" role granting read-only access to non-admin areas. Their assigned roles (SecurityManager, Auditor, etc.) are available but dormant until explicitly activated. This follows the principle of least privilege and prevents accidental privileged actions. All activations reset on logout.

## Requirements

- Users start each session with only the implicit "User" role (read-only)
- Users can activate/deactivate any role assigned to them for the current tenant
- Multiple roles can be active simultaneously
- The API must verify that each activated role is actually assigned to the user (security-critical)
- Every activation and deactivation is audit-logged
- Active roles reset when the session ends (logout or expiry)
- The UI clearly indicates the current privilege level

## Backend

### New Endpoint: `POST /api/roles/activate`

**Request body:**
```json
{ "roles": ["SecurityManager", "Auditor"] }
```

This represents the desired set of active roles. An empty array deactivates all roles (back to User-only).

**Validation:**
1. Authenticate the user (existing middleware)
2. Resolve the current tenant (existing `TenantContext`)
3. Load the user's assigned roles for this tenant from `UserTenantRole` table
4. Verify every requested role exists in the assigned set — reject with `403 Forbidden` if any role is not assigned
5. Compare the new active set with the previous active set to determine what changed

**Response:** Returns the updated `CurrentUser` object (same shape as `getCurrentUser()`) so the frontend can update state without a round-trip.

**Error cases:**
- `401` — not authenticated
- `403` — one or more requested roles not assigned to user for this tenant
- `400` — invalid role name

### Session Changes

**New field on `SessionData`:**
```typescript
activeRoles?: string[]  // defaults to undefined (= no elevated roles = User only)
```

- `getCurrentUser()` returns both `roles` (all assigned) and `activeRoles` (currently elevated)
- On logout, session is destroyed — active roles reset automatically
- On session expiry + token refresh, `activeRoles` persists (same session)

### Authorization Changes

**`RoleRequirementHandler`** currently checks all assigned roles (from Entra claims + database). Change to:

1. Read `activeRoles` from the session/context
2. If `activeRoles` is set, check against that set only
3. The implicit "User" role is always considered active (grants read-only access via existing policies that have no role requirements)
4. If `activeRoles` is not set (empty/undefined), the user has no elevated roles — only policies with no role requirement pass

**No changes needed to:**
- Policy definitions in `Program.cs`
- `[Authorize(Policy = ...)]` attributes on controllers
- The `RoleRequirement` class itself

The handler is the single point where the check changes from "assigned roles" to "active roles."

### Audit Logging

Use the existing audit infrastructure. Log entries for role activation/deactivation:

- **EntityType:** `RoleActivation`
- **Action:** `Activated` or `Deactivated`
- **NewValues:** `{ "Role": "SecurityManager" }` (or the deactivated role)
- **UserId, TenantId, Timestamp:** standard audit fields

Each role change in a single request produces its own audit entry. Example: activating SecurityManager and deactivating Auditor in one call = 2 audit entries.

### Tenant Switching

When the user switches tenants:
- Active roles reset (different tenants may have different role assignments)
- The frontend calls `POST /api/roles/activate` with an empty set or the user re-activates roles for the new tenant

## Frontend

### User Menu Change (TopNav)

Add an "Activate Roles..." item to the existing user dropdown menu, positioned after the profile section and before the theme selector:

```
┌──────────────────────┐
│ Frode Hus            │
│ frode@example.com    │
├──────────────────────┤
│ Activate Roles...    │  ← new item
│ Theme                │
│ Portal View          │
├──────────────────────┤
│ Log out              │
└──────────────────────┘
```

When roles are active, the menu item shows a count: "Activate Roles (2)"

### RoleActivationDialog Component

A modal dialog opened from the menu item.

**Content:**
- Title: "Activate Roles"
- Subtitle: "Elevated roles grant additional permissions. Active roles reset when you log out."
- List of all assigned roles for the current tenant, each with:
  - Role name (e.g., "Security Manager" — display-friendly)
  - Toggle switch reflecting active state
- Footer: muted text "Active roles reset when you log out"

**Behavior:**
- Each toggle immediately calls `POST /api/roles/activate` with the updated full set
- On success: update the user context, show toast ("Security Manager activated" / "Security Manager deactivated")
- On error: revert the toggle, show error toast
- The dialog stays open so users can toggle multiple roles without re-opening

**Edge cases:**
- If the user has no assigned roles (only User), the dialog shows: "No elevated roles are assigned to your account. Contact your administrator to request role access."
- If the API returns 403 (role was unassigned while dialog was open), show error toast and refresh the role list

### Role Indicator in TopNav

When any elevated role is active, show a visual indicator:
- A small dot or badge on or near the user avatar
- Primary color (lime) when elevated, absent when in User-only mode
- Provides at-a-glance awareness of privilege level

### Frontend Role Gating Changes

All existing `user.roles.includes(...)` checks change to use `user.activeRoles`:

- `Sidebar.tsx` — `canAccess()` function checks `activeRoles`
- `TopNav.tsx` — portal view switcher, OpenBao controls check `activeRoles`
- Dashboard views — mode availability checks `activeRoles`
- Any component that gates on roles

The `CurrentUser` type gains a new field:
```typescript
activeRoles: string[]  // currently elevated roles
```

### What the "User" Base Role Sees

With no roles activated, users see:
- Dashboard (read-only, default view only)
- Vulnerabilities (read-only browse)
- Devices (read-only browse)
- Software (read-only browse)
- No access to: Remediation, Approvals, Audit Trail, Settings, Admin Console

This matches routes/features that have no role requirement in the sidebar nav config.

## Out of Scope

- Time-limited role activation (roles last until logout)
- Role activation reasons/justification
- Per-tenant activation policy configuration
- Notification to admins when roles are activated
- Changes to how roles are assigned (admin panel unchanged)
