# TanStack Start Migration Design

## Goal

Migrate the Vigil frontend from a pure client-side Vite SPA to a TanStack Start full-stack application, gaining SSR for better initial load performance and server functions as a BFF to eliminate CORS and simplify auth.

## Architecture

```
Browser  ←→  TanStack Start (Node.js)  ←→  .NET API  ←→  PostgreSQL
                   │
                   ├── SSR (renders pages server-side)
                   ├── Server Functions (BFF — proxies to .NET API)
                   ├── Session Management (HttpOnly cookies, Entra ID OIDC)
                   ├── SSE endpoint (replaces SignalR)
                   └── Only externally exposed service
```

- **Start** is the single entry point for all client traffic — pages, data, and real-time events.
- **.NET API** becomes internal-only (not port-mapped in Docker).
- **Browser never talks to .NET API directly** — no CORS needed.
- **No MSAL in the browser** — auth is server-side confidential client flow.

## Authentication

**Current:** MSAL.js in browser → redirect to Entra → browser gets tokens → attaches Bearer header to API calls.

**New:** Confidential client OIDC flow entirely server-side.

1. User clicks "Sign in" → Start redirects to Entra `/authorize` endpoint.
2. Entra redirects back to Start's callback route with an auth code.
3. Start exchanges the auth code for tokens server-side (using client secret — never exposed to browser).
4. Start stores the access token + refresh token in an encrypted HttpOnly session cookie.
5. All subsequent requests include the cookie automatically — Start's auth middleware reads it, attaches the Bearer token when proxying to the .NET API.
6. Token refresh happens server-side transparently.

### Implementation Details

- TanStack Start middleware runs before every server function — reads session, validates token expiry, refreshes if needed.
- Auth callback handled via a server route (`/auth/callback`).
- Login/logout are server routes that redirect to Entra.
- Session library: `iron-session` or similar for encrypted cookie storage (no Redis needed).
- The .NET API auth config stays unchanged — it still validates Bearer JWTs from Entra.

### What Gets Removed

- `@azure/msal-browser` and `@azure/msal-react` packages.
- All client-side auth code (`lib/auth.ts`, `AuthProvider`, `useCurrentUser` query, `getAccessToken`).
- CORS configuration on the .NET API.

## Data Fetching — Server Functions as BFF

**Current:** Components call TanStack Query hooks → `apiClient.get()` → browser fetches from .NET API with Bearer token.

**New:** Two layers — server functions for data fetching, route loaders for SSR.

### Server Functions

```typescript
createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const res = await fetch(`${API_BASE}/vulnerabilities?...`, {
      headers: { Authorization: `Bearer ${context.token}` }
    })
    return res.json()
  })
```

### Route Loaders for SSR

```typescript
export const Route = createFileRoute('/vulnerabilities/')({
  loader: () => fetchVulnerabilities({ ... }),
  component: VulnerabilitiesPage,
})
```

### What Changes

- `src/api/` hooks rewritten as server functions — Zod validation stays, but runs server-side.
- Route files gain `loader` functions that call server functions → data available on first render (SSR).
- TanStack Query remains for client-side cache, mutations, and refetching — but initial data comes from the loader.
- `src/lib/api-client.ts` replaced by a simple server-side fetch wrapper.

### What Stays the Same

- Zod schemas for response validation.
- Query key patterns for cache invalidation.
- Mutation hooks (wrap server function calls instead of `apiClient`).
- All UI components unchanged.

## Real-Time — SSE Replacing SignalR

**Current:** Browser connects to .NET API via SignalR WebSocket for `NotificationCountUpdated`, `CriticalVulnerabilityDetected`, `TaskStatusChanged`.

**New:** Start exposes an SSE endpoint. The .NET API pushes events to Start internally.

### Flow

1. Browser opens `EventSource` connection to Start's SSE server route (`/api/events`).
2. Start's SSE route reads the session cookie to identify the user/tenant.
3. .NET API pushes events to Start via an internal HTTP endpoint.
4. Start forwards matching events to the connected browser.

### .NET → Start Communication

- .NET API calls a webhook-style internal endpoint on Start (`/api/internal/events`) when something happens.
- Start maintains an in-memory map of connected SSE clients (userId → response stream).
- Start matches incoming events to connected clients and writes to their SSE streams.

### What Gets Removed

- `@microsoft/signalr` package.
- `src/lib/signalr.ts`, `src/hooks/useSignalR.ts`.
- SignalR hub in .NET API (`NotificationHub`, `SignalRNotifier`).

### What Replaces It

- A server route in Start for SSE (`/api/events`).
- A React hook `useSSE(event, handler)` on the client.
- A lightweight event endpoint on Start that the .NET API calls internally.

## Deployment & Docker

**Current:** 3 external containers — nginx (port 3000), .NET API (port 8080), PostgreSQL (port 5432). Worker is internal.

**New:** 2 external containers — TanStack Start (port 3000), PostgreSQL (port 5432). API and Worker are internal.

```yaml
services:
  postgres:
    ports: ["5432:5432"]

  api:
    # .NET API — internal only, no port mapping
    # No CORS config needed

  worker:
    # unchanged — internal only

  frontend:
    # TanStack Start — Node.js server
    environment:
      API_BASE_URL: http://api:8080/api
      SESSION_SECRET: ${SESSION_SECRET}
      ENTRA_CLIENT_ID: ${AZURE_AD_CLIENT_ID}
      ENTRA_CLIENT_SECRET: ${ENTRA_CLIENT_SECRET}
      ENTRA_TENANT_ID: ${AZURE_AD_TENANT_ID}
      ENTRA_REDIRECT_URI: ${FRONTEND_ORIGIN}/auth/callback
    ports: ["3000:3000"]
```

### Dockerfile

- Base image: `node:22-alpine` (runtime, not just build stage).
- No nginx — Start is the server.
- Runtime env vars (not bake-time `VITE_*` vars).
- Multi-stage: build stage compiles, runtime stage runs `node .output/server/index.mjs`.

## Migration Scope

### Reuse As-Is (Copy Over)

- All `src/components/features/` — dashboard, vulnerabilities, tasks, campaigns, assets, audit, admin, settings, setup.
- All `src/components/layout/` — AppShell, Sidebar, TopNav, TenantSelector, NotificationBell (minus auth-specific props).
- Zod schemas from `src/api/` hooks.
- `src/types/api.ts`.
- `src/lib/utils.ts` (cn utility).
- Tailwind CSS setup, `index.css`.
- All shadcn/ui components.

### Rebuild from Scratch

- Route files — add `loader` functions, remove client-side auth guards.
- Data layer — server functions replacing `src/api/` hooks and `src/lib/api-client.ts`.
- Auth — server-side OIDC flow replacing MSAL browser.
- Real-time — SSE replacing SignalR.
- Dockerfile — Node.js runtime replacing nginx.
- Root layout — auth check via server-side session instead of `useCurrentUser` query.

### Delete Entirely

- `@azure/msal-browser`, `@azure/msal-react`, `@microsoft/signalr`.
- `src/lib/auth.ts`, `src/lib/signalr.ts`.
- `src/hooks/useSignalR.ts`, `src/hooks/useCurrentUser.ts`.
- `frontend/nginx.conf`, `frontend/.dockerignore` (rewrite for Node).

### New Additions

- `@tanstack/react-start` package.
- `iron-session` or equivalent for encrypted cookies.
- Server-side Entra OIDC client (e.g., `openid-client` or `arctic`).
- Auth middleware, session helpers.
- SSE server route + client hook.
- Internal event receiver endpoint.
