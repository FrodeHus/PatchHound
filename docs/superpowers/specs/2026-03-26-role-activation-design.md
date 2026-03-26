# Role Activation Design

## Overview

Users log in with a baseline "Stakeholder" role granting read-only access to non-admin areas. Their assigned roles (SecurityManager, Auditor, etc.) are available but dormant until explicitly activated. This follows the principle of least privilege and prevents accidental privileged actions. All activations reset on logout.

## Requirements

- Users start each session with the "Stakeholder" role always active (read-only, non-deactivatable)
- Users can activate/deactivate any other role assigned to them for the current tenant
- Multiple roles can be active simultaneously
- The API must verify that each activated role is actually assigned to the user (security-critical)
- Every activation and deactivation is audit-logged
- Active roles reset when the session ends (logout or expiry)
- The UI clearly indicates the current privilege level

## Architecture: Two-Layer Role Activation

Role activation spans two systems: the frontend session (iron-session in PostgreSQL) and the backend API (.NET). These systems have separate session stores and cannot directly share state. The design uses a two-layer approach:

1. **Frontend server function** handles the activation request, validates against the backend API, stores `activeRoles` in the frontend session, and returns the updated user.
2. **Frontend middleware** sends `activeRoles` to the backend on every API request via an `X-Active-Roles` header.
3. **Backend `RoleRequirementHandler`** reads the header, validates each role is assigned to the user (preventing header spoofing), and authorizes against only the active set.

This keeps the frontend session as the source of truth for active roles, while the backend independently validates on every request.

## Frontend Server

### New Server Function: `activateRoles`

A TanStack server function (in `frontend/src/api/roles.functions.ts`) that:

1. Reads the current session
2. Calls the backend `POST /api/roles/activate` with the requested roles — this validates assignment and writes audit logs
3. On success, stores `activeRoles` in the frontend session and calls `session.save()`
4. Returns the updated `CurrentUser`

**Request:** `{ roles: string[] }` — the desired set of active elevated roles (not including Stakeholder, which is always active). Empty array = deactivate all elevated roles.

**Error handling:**
- Backend returns `403` → role not assigned → return error to UI, don't update session
- Backend returns `400` → invalid role name → return error to UI

### Session Changes

**New field on `SessionData`:**
```typescript
activeRoles?: string[]  // defaults to undefined (= no elevated roles = Stakeholder only)
```

- `getCurrentUser()` returns both `roles` (all assigned) and `activeRoles` (currently elevated, defaults to `[]`)
- `session.save()` must persist `activeRoles` (add to the explicit field list in save method)
- On logout, session is destroyed — active roles reset automatically
- On token refresh, `activeRoles` persists within the same session

### API Request Header

Active roles must be sent to the backend on every API request. This happens in `frontend/src/server/api.ts`:

1. Add `activeRoles` to the `ApiRequestContext` type
2. In `buildHeaders()`, add the `X-Active-Roles` header from the context:
   ```
   X-Active-Roles: SecurityManager,Auditor
   ```
   Empty or absent header = no elevated roles (Stakeholder only).

3. In `frontend/src/server/middleware.ts`, add `activeRoles` to the context passed to `next()` so server functions have access to it from the session:
   ```typescript
   activeRoles: session.activeRoles ?? []
   ```

This ensures the backend receives the active role set on every API call and can validate it independently.

## Backend API

### New Endpoint: `POST /api/roles/activate`

**Controller:** New `RolesController` (or added to existing auth-related controller).

**Request body:**
```json
{ "roles": ["SecurityManager", "Auditor"] }
```

**Logic:**
1. Authenticate the user (existing middleware)
2. Resolve the current tenant (existing `TenantContext`)
3. Load the user's assigned roles for this tenant from `UserTenantRole` table
4. Verify every requested role exists in the assigned set — reject with `403 Forbidden` if any role is not assigned
5. Read the previous active roles from the `X-Active-Roles` header (the current state)
6. Compare old and new sets, write audit log entries for each change
7. Return `200 OK` with the validated role list

**Authorization:** `[Authorize]` only (any authenticated user). No policy required — the endpoint validates role assignment server-side.

**Error cases:**
- `401` — not authenticated
- `403` — one or more requested roles not assigned to user for this tenant
- `400` — invalid role name

**Idempotency:** Activating an already-active role is a no-op (no audit entry for that role).

**Header on activation call:** The `activateRoles` server function must include the current `X-Active-Roles` header (from the session, before updating) so the backend can compute the diff for audit logging.

### Authorization Changes

**`RoleRequirementHandler`** currently checks all assigned roles (from Entra claims + database). Change to:

1. Read the `X-Active-Roles` header from the HTTP request
2. If the header is present and non-empty, parse it into a role list
3. Validate each role in the header is actually assigned to the user for the current tenant (prevents header spoofing — this is security-critical)
4. Authorize against only the validated active roles
5. **Always include Stakeholder** in the effective role set, regardless of the header. Stakeholder is permanent and cannot be deactivated.
6. If the header is absent or empty, the user has only Stakeholder — policies requiring Stakeholder pass, all others fail.

The Stakeholder role provides baseline read-only access (viewing vulnerabilities, devices, software).

**No changes needed to:**
- Policy definitions in `Program.cs`
- `[Authorize(Policy = ...)]` attributes on controllers
- The `RoleRequirement` class itself

The handler is the single point where the check changes from "assigned roles" to "active roles."

### Audit Logging

Use the existing audit infrastructure. Log entries for role activation/deactivation:

- **EntityType:** `RoleActivation`
- **Action:** `Activated` or `Deactivated`
- **NewValues:** `{ "Role": "SecurityManager" }` (for activation)
- **OldValues:** `{ "Role": "Auditor" }` (for deactivation)
- **UserId, TenantId, Timestamp:** standard audit fields

Each role change in a single request produces its own audit entry. Example: activating SecurityManager and deactivating Auditor in one call = 2 audit entries. Re-activating an already-active role produces no audit entry.

### Tenant Switching

When the user switches tenants:
- Frontend clears `activeRoles` from the session (different tenants may have different role assignments)
- The user must re-activate roles for the new tenant

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
- Stakeholder role shown but non-toggleable (always active, visually distinct — greyed-out toggle or "Always active" label)
- List of all other assigned roles for the current tenant, each with:
  - Role name (e.g., "Security Manager" — display-friendly)
  - Toggle switch reflecting active state
- Footer: muted text "Active roles reset when you log out"

**Behavior:**
- Each toggle immediately calls `POST /api/roles/activate` with the updated full set
- On success: update the user context, show toast ("Security Manager activated" / "Security Manager deactivated")
- On error: revert the toggle, show error toast
- The dialog stays open so users can toggle multiple roles without re-opening

**Edge cases:**
- If the user has no assigned roles beyond Stakeholder, the dialog shows only Stakeholder (always active) and a message: "No additional roles are assigned to your account. Contact your administrator to request role access."
- If the API returns 403 (role was unassigned while dialog was open), show error toast and refresh the role list

### Role Indicator in TopNav

When any elevated role is active (beyond Stakeholder), show a visual indicator:
- A small dot or badge on or near the user avatar
- Primary color (lime) when elevated, absent when in Stakeholder-only mode
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

### What Stakeholder-Only Sees

With no elevated roles activated (Stakeholder only), users see:
- Dashboard (read-only, default view only)
- Vulnerabilities (read-only browse)
- Devices (read-only browse)
- Software (read-only browse)
- No access to: Remediation, Approvals, Audit Trail, Settings, Admin Console

This matches routes/features that require only the Stakeholder role or no specific role in the sidebar nav config.

### GlobalAdmin Treatment

GlobalAdmin follows the same activation pattern — it must be explicitly activated. There is no special treatment. This means:
- OpenBao unseal (TopNav) requires GlobalAdmin to be active
- Portal view switcher requires GlobalAdmin to be active
- Admin user management requires GlobalAdmin to be active

This is intentional: even administrators should operate with least privilege by default.

## Out of Scope

- Time-limited role activation (roles last until logout)
- Role activation reasons/justification
- Per-tenant activation policy configuration
- Notification to admins when roles are activated
- Changes to how roles are assigned (admin panel unchanged)
