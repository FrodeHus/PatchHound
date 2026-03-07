# TanStack Start Migration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Migrate the Vigil frontend from a Vite SPA to a TanStack Start full-stack app with SSR, server-side auth, and BFF pattern.

**Architecture:** TanStack Start acts as the sole external-facing service. It handles SSR, server-side OIDC auth with Entra ID (encrypted session cookies), BFF server functions that proxy to the internal .NET API, and SSE for real-time events replacing SignalR.

**Tech Stack:** TanStack Start, TanStack Router, TanStack Query, React 19, Tailwind CSS 4, Zod, iron-session, openid-client (Entra ID OIDC), Nitro (Node.js deployment), Vitest

**Design Doc:** `docs/plans/2026-03-07-tanstack-start-migration-design.md`

---

## Phase 1: Scaffold TanStack Start Project

### Task 1: Initialize New TanStack Start Project

**Files:**
- Create: `frontend-start/package.json`
- Create: `frontend-start/vite.config.ts`
- Create: `frontend-start/tsconfig.json`

**Step 1: Create the project directory**

```bash
mkdir -p frontend-start
cd frontend-start
```

**Step 2: Create package.json**

```json
{
  "name": "vigil-frontend",
  "private": true,
  "type": "module",
  "scripts": {
    "dev": "vite dev",
    "build": "vite build",
    "start": "node .output/server/index.mjs",
    "test": "vitest run",
    "lint": "eslint ."
  },
  "dependencies": {
    "@tanstack/react-query": "^5.90.21",
    "@tanstack/react-router": "^1.166.2",
    "@tanstack/react-start": "^1.166.2",
    "react": "^19.2.0",
    "react-dom": "^19.2.0",
    "zod": "^4.3.6",
    "class-variance-authority": "^0.7.1",
    "clsx": "^2.1.1",
    "lucide-react": "^0.577.0",
    "radix-ui": "^1.4.3",
    "recharts": "^3.7.0",
    "tailwind-merge": "^3.5.0",
    "tailwindcss": "^4.2.1",
    "iron-session": "^8.0.4",
    "openid-client": "^6.4.3"
  },
  "devDependencies": {
    "@tailwindcss/vite": "^4.2.1",
    "@tanstack/router-plugin": "^1.166.2",
    "@testing-library/jest-dom": "^6.9.1",
    "@testing-library/react": "^16.3.2",
    "@types/node": "^24.10.1",
    "@types/react": "^19.2.7",
    "@types/react-dom": "^19.2.3",
    "@vitejs/plugin-react": "^5.1.1",
    "nitro": "npm:nitro-nightly@latest",
    "typescript": "~5.9.3",
    "vite": "^7.3.1",
    "vite-tsconfig-paths": "^4.3.2",
    "vitest": "^4.0.18",
    "jsdom": "^28.1.0"
  }
}
```

**Step 3: Create vite.config.ts**

```typescript
/// <reference types="vitest" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import tsConfigPaths from 'vite-tsconfig-paths'
import { tanstackStart } from '@tanstack/react-start/plugin/vite'
import { nitro } from 'nitro/vite'

export default defineConfig({
  server: { port: 3000 },
  plugins: [
    tsConfigPaths({ projects: ['./tsconfig.json'] }),
    tanstackStart(),
    react(),
    tailwindcss(),
    nitro(),
  ],
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts'],
  },
})
```

**Step 4: Create tsconfig.json**

```json
{
  "compilerOptions": {
    "jsx": "react-jsx",
    "moduleResolution": "Bundler",
    "module": "ESNext",
    "target": "ES2022",
    "skipLibCheck": true,
    "strict": true,
    "baseUrl": ".",
    "paths": {
      "@/*": ["./src/*"]
    }
  },
  "include": ["src"]
}
```

**Step 5: Install dependencies**

```bash
npm install
```

**Step 6: Commit**

```bash
git add frontend-start/
git commit -m "feat: scaffold TanStack Start project"
```

---

### Task 2: Create App Shell and Root Route

The root route in TanStack Start renders the full HTML document (html, head, body). This replaces `index.html` from Vite.

**Files:**
- Create: `frontend-start/src/router.tsx`
- Create: `frontend-start/src/routes/__root.tsx`
- Create: `frontend-start/src/routeTree.gen.ts` (auto-generated on first run, but create placeholder)
- Copy: `frontend/src/index.css` → `frontend-start/src/styles/app.css`
- Copy: `frontend/src/lib/utils.ts` → `frontend-start/src/lib/utils.ts`
- Create: `frontend-start/src/test-setup.ts`

**Step 1: Create the router factory**

```typescript
// frontend-start/src/router.tsx
import { createRouter as createTanStackRouter } from '@tanstack/react-router'
import { routeTree } from './routeTree.gen'

export function createRouter() {
  return createTanStackRouter({
    routeTree,
    scrollRestoration: true,
  })
}

declare module '@tanstack/react-router' {
  interface Register {
    router: ReturnType<typeof createRouter>
  }
}
```

**Step 2: Create the root route**

The root route in TanStack Start must render the full HTML document including `<html>`, `<head>`, and `<body>` tags. It uses `<HeadContent />` for metadata and `<Scripts />` for framework scripts.

```tsx
// frontend-start/src/routes/__root.tsx
import {
  createRootRoute,
  HeadContent,
  Outlet,
  Scripts,
} from '@tanstack/react-router'

export const Route = createRootRoute({
  head: () => ({
    meta: [
      { charSet: 'utf-8' },
      { name: 'viewport', content: 'width=device-width, initial-scale=1' },
      { title: 'Vigil — Vulnerability Management' },
    ],
    links: [
      { rel: 'icon', href: '/favicon.ico' },
    ],
  }),
  component: RootDocument,
})

function RootDocument() {
  return (
    <html lang="en">
      <head>
        <HeadContent />
      </head>
      <body className="min-h-screen bg-background text-foreground antialiased">
        <Outlet />
        <Scripts />
      </body>
    </html>
  )
}
```

**Step 3: Copy styles and utilities**

```bash
mkdir -p frontend-start/src/styles frontend-start/src/lib
cp frontend/src/index.css frontend-start/src/styles/app.css
cp frontend/src/lib/utils.ts frontend-start/src/lib/utils.ts
```

Import the CSS in the root route's `head` by adding to the `links` array:
```typescript
links: [
  { rel: 'stylesheet', href: '/src/styles/app.css' },
],
```

**Step 4: Create test setup**

```typescript
// frontend-start/src/test-setup.ts
import '@testing-library/jest-dom/vitest'
```

**Step 5: Create a minimal index route to verify the app runs**

```tsx
// frontend-start/src/routes/index.tsx
import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/')({
  component: () => <h1>Vigil</h1>,
})
```

**Step 6: Run dev server to verify it starts**

```bash
cd frontend-start && npm run dev
```

Expected: App loads at `http://localhost:3000` showing "Vigil".

**Step 7: Commit**

```bash
git add frontend-start/src/
git commit -m "feat: add root route, router, and styles"
```

---

## Phase 2: Server-Side Authentication

### Task 3: Session Helpers

**Files:**
- Create: `frontend-start/src/server/session.ts`

**Step 1: Create the session helper**

This wraps `iron-session` for encrypted HttpOnly cookie sessions. The session stores the Entra ID access token, refresh token, and basic user info.

```typescript
// frontend-start/src/server/session.ts
import { getIronSession } from 'iron-session'
import { getWebRequest } from '@tanstack/react-start/server'

export interface SessionData {
  accessToken?: string
  refreshToken?: string
  tokenExpiry?: number
  userId?: string
  email?: string
  displayName?: string
  tenantId?: string
  roles?: string[]
  tenantIds?: string[]
}

const sessionOptions = {
  password: process.env.SESSION_SECRET!,
  cookieName: 'vigil-session',
  cookieOptions: {
    secure: process.env.NODE_ENV === 'production',
    httpOnly: true,
    sameSite: 'lax' as const,
    maxAge: 7 * 24 * 60 * 60, // 7 days
  },
}

export async function getSession() {
  const request = getWebRequest()
  // iron-session works with a request/response pair
  // For reading, we parse the cookie from the request headers
  const cookieHeader = request.headers.get('cookie') ?? ''
  const mockReq = { headers: { cookie: cookieHeader } } as any
  const mockRes = { getHeader: () => '', setHeader: () => {} } as any
  return getIronSession<SessionData>(mockReq, mockRes, sessionOptions)
}

export function isTokenExpired(session: SessionData): boolean {
  if (!session.tokenExpiry) return true
  // Refresh 5 minutes before expiry
  return Date.now() > (session.tokenExpiry - 5 * 60 * 1000)
}
```

> **Note:** The exact iron-session integration with TanStack Start may need adjustment based on the version. TanStack Start also provides a built-in `useSession` from `@tanstack/react-start/server` — prefer that if it meets the needs. The implementer should check TanStack Start's auth docs and use whichever session approach works best. The key requirement is: encrypted HttpOnly cookie storing access token, refresh token, and user info.

**Step 2: Commit**

```bash
git add frontend-start/src/server/
git commit -m "feat: add session helpers for encrypted cookie storage"
```

---

### Task 4: Entra ID OIDC Server Routes

**Files:**
- Create: `frontend-start/src/server/auth.ts`
- Create: `frontend-start/src/routes/auth/login.ts`
- Create: `frontend-start/src/routes/auth/callback.ts`
- Create: `frontend-start/src/routes/auth/logout.ts`

**Step 1: Create the Entra ID OIDC helper**

This handles the OAuth2 authorization code flow with PKCE server-side.

```typescript
// frontend-start/src/server/auth.ts

const ENTRA_CLIENT_ID = process.env.ENTRA_CLIENT_ID!
const ENTRA_CLIENT_SECRET = process.env.ENTRA_CLIENT_SECRET!
const ENTRA_TENANT_ID = process.env.ENTRA_TENANT_ID!
const ENTRA_REDIRECT_URI = process.env.ENTRA_REDIRECT_URI!
const ENTRA_SCOPES = process.env.ENTRA_SCOPES ?? 'openid profile email'

const AUTHORITY = `https://login.microsoftonline.com/${ENTRA_TENANT_ID}/oauth2/v2.0`

export function getAuthorizationUrl(state: string): string {
  const params = new URLSearchParams({
    client_id: ENTRA_CLIENT_ID,
    response_type: 'code',
    redirect_uri: ENTRA_REDIRECT_URI,
    scope: ENTRA_SCOPES,
    response_mode: 'query',
    state,
  })
  return `${AUTHORITY}/authorize?${params.toString()}`
}

export async function exchangeCodeForTokens(code: string) {
  const params = new URLSearchParams({
    client_id: ENTRA_CLIENT_ID,
    client_secret: ENTRA_CLIENT_SECRET,
    grant_type: 'authorization_code',
    code,
    redirect_uri: ENTRA_REDIRECT_URI,
    scope: ENTRA_SCOPES,
  })

  const response = await fetch(`${AUTHORITY}/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: params.toString(),
  })

  if (!response.ok) {
    const error = await response.text()
    throw new Error(`Token exchange failed: ${error}`)
  }

  return response.json() as Promise<{
    access_token: string
    refresh_token?: string
    expires_in: number
    id_token: string
  }>
}

export async function refreshAccessToken(refreshToken: string) {
  const params = new URLSearchParams({
    client_id: ENTRA_CLIENT_ID,
    client_secret: ENTRA_CLIENT_SECRET,
    grant_type: 'refresh_token',
    refresh_token: refreshToken,
    scope: ENTRA_SCOPES,
  })

  const response = await fetch(`${AUTHORITY}/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: params.toString(),
  })

  if (!response.ok) {
    throw new Error('Token refresh failed')
  }

  return response.json() as Promise<{
    access_token: string
    refresh_token?: string
    expires_in: number
  }>
}

export function parseIdToken(idToken: string): {
  oid?: string
  preferred_username?: string
  name?: string
  tid?: string
  roles?: string[]
} {
  const payload = idToken.split('.')[1]
  return JSON.parse(Buffer.from(payload, 'base64url').toString())
}
```

**Step 2: Create login server route**

```typescript
// frontend-start/src/routes/auth/login.ts
import { createFileRoute, redirect } from '@tanstack/react-router'
import { getAuthorizationUrl } from '@/server/auth'

export const Route = createFileRoute('/auth/login')({
  server: {
    handlers: {
      GET: async () => {
        const state = crypto.randomUUID()
        const url = getAuthorizationUrl(state)
        return Response.redirect(url, 302)
      },
    },
  },
})
```

**Step 3: Create callback server route**

```typescript
// frontend-start/src/routes/auth/callback.ts
import { createFileRoute } from '@tanstack/react-router'
import { exchangeCodeForTokens, parseIdToken } from '@/server/auth'
import { getSession } from '@/server/session'

export const Route = createFileRoute('/auth/callback')({
  server: {
    handlers: {
      GET: async ({ request }) => {
        const url = new URL(request.url)
        const code = url.searchParams.get('code')
        const error = url.searchParams.get('error')

        if (error || !code) {
          return Response.redirect('/?error=auth_failed', 302)
        }

        const tokens = await exchangeCodeForTokens(code)
        const claims = parseIdToken(tokens.id_token)

        const session = await getSession()
        session.accessToken = tokens.access_token
        session.refreshToken = tokens.refresh_token
        session.tokenExpiry = Date.now() + tokens.expires_in * 1000
        session.userId = claims.oid
        session.email = claims.preferred_username
        session.displayName = claims.name
        session.tenantId = claims.tid
        session.roles = claims.roles ?? []
        await session.save()

        return Response.redirect('/', 302)
      },
    },
  },
})
```

**Step 4: Create logout server route**

```typescript
// frontend-start/src/routes/auth/logout.ts
import { createFileRoute } from '@tanstack/react-router'
import { getSession } from '@/server/session'

export const Route = createFileRoute('/auth/logout')({
  server: {
    handlers: {
      GET: async () => {
        const session = await getSession()
        session.destroy()

        const tenantId = process.env.ENTRA_TENANT_ID
        const postLogoutUri = encodeURIComponent(process.env.FRONTEND_ORIGIN ?? 'http://localhost:3000')
        const logoutUrl = `https://login.microsoftonline.com/${tenantId}/oauth2/v2.0/logout?post_logout_redirect_uri=${postLogoutUri}`

        return Response.redirect(logoutUrl, 302)
      },
    },
  },
})
```

**Step 5: Commit**

```bash
git add frontend-start/src/server/ frontend-start/src/routes/auth/
git commit -m "feat: add Entra ID OIDC auth routes (login, callback, logout)"
```

---

### Task 5: Auth Middleware for Server Functions

**Files:**
- Create: `frontend-start/src/server/middleware.ts`
- Create: `frontend-start/src/server/api.ts`

**Step 1: Create auth middleware**

This middleware runs before every server function that needs authentication. It reads the session, refreshes expired tokens, and provides the access token in context.

```typescript
// frontend-start/src/server/middleware.ts
import { createMiddleware } from '@tanstack/react-start'
import { getSession, isTokenExpired } from '@/server/session'
import { refreshAccessToken } from '@/server/auth'
import { redirect } from '@tanstack/react-router'

export const authMiddleware = createMiddleware({ type: 'function' })
  .server(async ({ next }) => {
    const session = await getSession()

    if (!session.accessToken) {
      throw redirect({ to: '/auth/login' })
    }

    // Refresh token if expired
    if (isTokenExpired(session) && session.refreshToken) {
      try {
        const tokens = await refreshAccessToken(session.refreshToken)
        session.accessToken = tokens.access_token
        if (tokens.refresh_token) {
          session.refreshToken = tokens.refresh_token
        }
        session.tokenExpiry = Date.now() + tokens.expires_in * 1000
        await session.save()
      } catch {
        // Refresh failed — force re-login
        session.destroy()
        throw redirect({ to: '/auth/login' })
      }
    }

    return next({
      context: {
        token: session.accessToken,
        userId: session.userId,
        tenantId: session.tenantId,
        roles: session.roles ?? [],
      },
    })
  })
```

**Step 2: Create API helper for proxying to .NET API**

```typescript
// frontend-start/src/server/api.ts

const API_BASE = process.env.API_BASE_URL ?? 'http://localhost:8080/api'

export async function apiGet<T>(path: string, token: string): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    headers: { Authorization: `Bearer ${token}` },
  })

  if (!response.ok) {
    throw new Error(`API error: ${response.status} ${response.statusText}`)
  }

  if (response.status === 204) {
    return null as T
  }

  return response.json() as Promise<T>
}

export async function apiPost<T>(path: string, token: string, body?: unknown): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  if (!response.ok) {
    throw new Error(`API error: ${response.status} ${response.statusText}`)
  }

  if (response.status === 204) {
    return null as T
  }

  return response.json() as Promise<T>
}

export async function apiPut<T>(path: string, token: string, body?: unknown): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    method: 'PUT',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  if (!response.ok) {
    throw new Error(`API error: ${response.status} ${response.statusText}`)
  }

  if (response.status === 204) {
    return null as T
  }

  return response.json() as Promise<T>
}

export async function apiDelete<T>(path: string, token: string): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    method: 'DELETE',
    headers: { Authorization: `Bearer ${token}` },
  })

  if (!response.ok) {
    throw new Error(`API error: ${response.status} ${response.statusText}`)
  }

  if (response.status === 204) {
    return null as T
  }

  return response.json() as Promise<T>
}
```

**Step 3: Commit**

```bash
git add frontend-start/src/server/
git commit -m "feat: add auth middleware and API proxy helpers"
```

---

### Task 6: Current User Server Function and Auth Guard

**Files:**
- Create: `frontend-start/src/server/auth.functions.ts`
- Modify: `frontend-start/src/routes/__root.tsx`

**Step 1: Create getCurrentUser server function**

```typescript
// frontend-start/src/server/auth.functions.ts
import { createServerFn } from '@tanstack/react-start'
import { getSession } from '@/server/session'

export const getCurrentUser = createServerFn({ method: 'GET' })
  .handler(async () => {
    const session = await getSession()

    if (!session.accessToken || !session.userId) {
      return null
    }

    return {
      id: session.userId,
      email: session.email ?? '',
      displayName: session.displayName ?? '',
      roles: session.roles ?? [],
      tenantId: session.tenantId,
      tenantIds: session.tenantIds ?? (session.tenantId ? [session.tenantId] : []),
    }
  })

export type CurrentUser = NonNullable<Awaited<ReturnType<typeof getCurrentUser>>>
```

**Step 2: Update root route with auth guard using beforeLoad**

```tsx
// frontend-start/src/routes/__root.tsx
import {
  createRootRouteWithContext,
  HeadContent,
  Outlet,
  Scripts,
} from '@tanstack/react-router'
import { getCurrentUser, type CurrentUser } from '@/server/auth.functions'

interface RouterContext {
  user: CurrentUser | null
}

export const Route = createRootRouteWithContext<RouterContext>()({
  head: () => ({
    meta: [
      { charSet: 'utf-8' },
      { name: 'viewport', content: 'width=device-width, initial-scale=1' },
      { title: 'Vigil — Vulnerability Management' },
    ],
  }),
  beforeLoad: async () => {
    const user = await getCurrentUser()
    return { user }
  },
  component: RootDocument,
})

function RootDocument() {
  return (
    <html lang="en">
      <head>
        <HeadContent />
      </head>
      <body className="min-h-screen bg-background text-foreground antialiased">
        <Outlet />
        <Scripts />
      </body>
    </html>
  )
}
```

**Step 3: Create a pathless `_authed` layout that requires login**

```tsx
// frontend-start/src/routes/_authed.tsx
import { createFileRoute, Outlet, redirect } from '@tanstack/react-router'

export const Route = createFileRoute('/_authed')({
  beforeLoad: async ({ context }) => {
    if (!context.user) {
      throw redirect({ to: '/auth/login' })
    }
    return { user: context.user }
  },
  component: () => <Outlet />,
})
```

**Step 4: Create a login page for unauthenticated users**

```tsx
// frontend-start/src/routes/login.tsx
import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/login')({
  component: LoginPage,
})

function LoginPage() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-background">
      <div className="w-full max-w-sm space-y-6 text-center">
        <div className="space-y-2">
          <h1 className="text-3xl font-bold tracking-tight">Vigil</h1>
          <p className="text-muted-foreground">Vulnerability Management Platform</p>
        </div>
        <a
          href="/auth/login"
          className="inline-flex w-full items-center justify-center rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground shadow hover:bg-primary/90"
        >
          Sign in with Microsoft
        </a>
      </div>
    </div>
  )
}
```

**Step 5: Commit**

```bash
git add frontend-start/src/
git commit -m "feat: add auth guard with beforeLoad and login page"
```

---

## Phase 3: BFF Server Functions and Data Layer

### Task 7: Dashboard Server Functions and Route

This task establishes the pattern for all BFF server functions. Every subsequent data layer task follows this same pattern.

**Files:**
- Copy: Zod schemas from `frontend/src/api/useDashboard.ts` → `frontend-start/src/api/dashboard.schemas.ts`
- Create: `frontend-start/src/api/dashboard.functions.ts`
- Create: `frontend-start/src/routes/_authed/index.tsx`
- Copy: `frontend/src/components/features/dashboard/` → `frontend-start/src/components/features/dashboard/`

**Step 1: Extract and copy Zod schemas**

```typescript
// frontend-start/src/api/dashboard.schemas.ts
import { z } from 'zod'

export const topVulnerabilitySchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  title: z.string(),
  severity: z.string(),
  cvssScore: z.number().nullable(),
  affectedAssetCount: z.number(),
  daysSincePublished: z.number(),
})

export const dashboardSummarySchema = z.object({
  exposureScore: z.number(),
  vulnerabilitiesBySeverity: z.record(z.string(), z.number()),
  vulnerabilitiesByStatus: z.record(z.string(), z.number()),
  slaCompliancePercent: z.number(),
  overdueTaskCount: z.number(),
  totalTaskCount: z.number(),
  averageRemediationDays: z.number(),
  topCriticalVulnerabilities: z.array(topVulnerabilitySchema),
})

export const trendItemSchema = z.object({
  date: z.string(),
  severity: z.string(),
  count: z.number(),
})

export const trendDataSchema = z.object({
  items: z.array(trendItemSchema),
})

export type DashboardSummary = z.infer<typeof dashboardSummarySchema>
export type TopVulnerability = z.infer<typeof topVulnerabilitySchema>
export type TrendData = z.infer<typeof trendDataSchema>
export type TrendItem = z.infer<typeof trendItemSchema>
```

**Step 2: Create server functions**

```typescript
// frontend-start/src/api/dashboard.functions.ts
import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet } from '@/server/api'
import { dashboardSummarySchema, trendDataSchema } from './dashboard.schemas'

export const fetchDashboardSummary = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/dashboard/summary', context.token)
    return dashboardSummarySchema.parse(data)
  })

export const fetchDashboardTrends = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .handler(async ({ context }) => {
    const data = await apiGet('/dashboard/trends', context.token)
    return trendDataSchema.parse(data)
  })
```

**Step 3: Copy dashboard UI components**

```bash
mkdir -p frontend-start/src/components/features/dashboard
cp frontend/src/components/features/dashboard/*.tsx frontend-start/src/components/features/dashboard/
```

These components (ExposureScore, SlaComplianceCard, RemediationVelocity, TrendChart, CriticalVulnerabilities) are pure UI — they take props and render. No changes needed.

**Step 4: Create the dashboard route with SSR loader**

```tsx
// frontend-start/src/routes/_authed/index.tsx
import { createFileRoute } from '@tanstack/react-router'
import { fetchDashboardSummary, fetchDashboardTrends } from '@/api/dashboard.functions'
import { CriticalVulnerabilities } from '@/components/features/dashboard/CriticalVulnerabilities'
import { ExposureScore } from '@/components/features/dashboard/ExposureScore'
import { RemediationVelocity } from '@/components/features/dashboard/RemediationVelocity'
import { SlaComplianceCard } from '@/components/features/dashboard/SlaComplianceCard'
import { TrendChart } from '@/components/features/dashboard/TrendChart'

export const Route = createFileRoute('/_authed/')({
  loader: async () => {
    const [summary, trends] = await Promise.all([
      fetchDashboardSummary(),
      fetchDashboardTrends(),
    ])
    return { summary, trends }
  },
  component: DashboardPage,
})

function DashboardPage() {
  const { summary, trends } = Route.useLoaderData()

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Dashboard</h1>

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        <ExposureScore score={summary.exposureScore} />
        <SlaComplianceCard
          percent={summary.slaCompliancePercent}
          overdueCount={summary.overdueTaskCount}
          totalCount={summary.totalTaskCount}
        />
        <RemediationVelocity
          averageDays={summary.averageRemediationDays}
          vulnerabilitiesBySeverity={summary.vulnerabilitiesBySeverity}
        />
      </div>

      <div className="grid gap-4 xl:grid-cols-5">
        <div className="xl:col-span-3">
          <TrendChart data={trends} />
        </div>
        <div className="xl:col-span-2">
          <CriticalVulnerabilities items={summary.topCriticalVulnerabilities} />
        </div>
      </div>
    </section>
  )
}
```

**Step 5: Commit**

```bash
git add frontend-start/src/api/ frontend-start/src/components/ frontend-start/src/routes/
git commit -m "feat: add dashboard with SSR loader and BFF server functions"
```

---

### Task 8: Remaining Server Functions

Follow the exact same pattern from Task 7 for each feature area. For each:
1. Copy Zod schemas from `frontend/src/api/use<Feature>.ts` into `frontend-start/src/api/<feature>.schemas.ts`
2. Create server functions in `frontend-start/src/api/<feature>.functions.ts`
3. Copy UI components from `frontend/src/components/features/<feature>/`
4. Create route files under `frontend-start/src/routes/_authed/`

**Features to migrate (in order):**

| Feature | Schemas from | Server functions | Route(s) |
|---------|-------------|-----------------|----------|
| Vulnerabilities | `useVulnerabilities.ts` | `vulnerabilities.functions.ts` | `_authed/vulnerabilities/index.tsx`, `_authed/vulnerabilities/$id.tsx` |
| Tasks | `useTasks.ts` | `tasks.functions.ts` | `_authed/tasks/index.tsx` |
| Assets | `useAssets.ts` | `assets.functions.ts` | `_authed/assets/index.tsx` |
| Campaigns | `useCampaigns.ts` | `campaigns.functions.ts` | `_authed/campaigns/index.tsx`, `_authed/campaigns/$id.tsx` |
| Audit Log | `useAuditLog.ts` | `audit-log.functions.ts` | `_authed/audit-log/index.tsx` |
| Users | `useUsers.ts` | `users.functions.ts` | `_authed/admin/users.tsx` |
| Teams | `useTeams.ts` | `teams.functions.ts` | `_authed/admin/teams.tsx` |
| Settings | `useSettings.ts` | `settings.functions.ts` | `_authed/settings/index.tsx` |
| Setup | `useSetup.ts` | `setup.functions.ts` | `setup/index.tsx` (NOT under `_authed`) |

**Pattern for each server function file:**

```typescript
// Example: frontend-start/src/api/vulnerabilities.functions.ts
import { createServerFn } from '@tanstack/react-start'
import { authMiddleware } from '@/server/middleware'
import { apiGet, apiPost, apiPut } from '@/server/api'
import { vulnerabilityListSchema, vulnerabilityDetailSchema } from './vulnerabilities.schemas'

// Queries use method: 'GET'
export const fetchVulnerabilities = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(/* optional: zod schema for filters */)
  .handler(async ({ context, data }) => {
    const params = new URLSearchParams(/* build from data */)
    const result = await apiGet(`/vulnerabilities?${params}`, context.token)
    return vulnerabilityListSchema.parse(result)
  })

// Mutations use method: 'POST'
export const updateOrgSeverity = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .handler(async ({ context, data }) => {
    return apiPut(`/vulnerabilities/${data.id}/organizational-severity`, context.token, data.payload)
  })
```

**Pattern for each route file with SSR:**

```tsx
// List routes load data in the loader for SSR
export const Route = createFileRoute('/_authed/vulnerabilities/')({
  loader: () => fetchVulnerabilities({ data: { page: 1, pageSize: 50 } }),
  component: VulnerabilitiesPage,
})

// Detail routes load by param
export const Route = createFileRoute('/_authed/vulnerabilities/$id')({
  loader: ({ params }) => fetchVulnerabilityDetail({ data: params.id }),
  component: VulnerabilityDetailPage,
})
```

**Client-side mutations** still use TanStack Query's `useMutation` but call server functions instead of `apiClient`:

```typescript
// In a component
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { updateOrgSeverity } from '@/api/vulnerabilities.functions'

function MyComponent() {
  const mutation = useMutation({
    mutationFn: (data) => updateOrgSeverity({ data }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['vulnerabilities'] }),
  })
}
```

**Commit after each feature is complete.**

---

### Task 9: Copy Layout Components

**Files:**
- Copy: `frontend/src/components/layout/` → `frontend-start/src/components/layout/`

**Step 1: Copy layout components**

```bash
mkdir -p frontend-start/src/components/layout
cp frontend/src/components/layout/*.tsx frontend-start/src/components/layout/
```

**Step 2: Update AppShell**

Remove MSAL-specific auth props. The user comes from router context now:

```tsx
// Modify AppShell.tsx
// Remove: import { useAuthActions, useCurrentUser } from '@/hooks/useCurrentUser'
// Replace with: import { useRouteContext } from '@tanstack/react-router'
// Access user via: const { user } = Route.useRouteContext() (or passed as prop from _authed layout)
// Login: <a href="/auth/login">
// Logout: <a href="/auth/logout">
```

**Step 3: Update the `_authed` layout to wrap with AppShell**

```tsx
// frontend-start/src/routes/_authed.tsx
import { createFileRoute, Outlet, redirect } from '@tanstack/react-router'
import { AppShell } from '@/components/layout/AppShell'

export const Route = createFileRoute('/_authed')({
  beforeLoad: async ({ context }) => {
    if (!context.user) {
      throw redirect({ to: '/login' })
    }
    return { user: context.user }
  },
  component: AuthedLayout,
})

function AuthedLayout() {
  const { user } = Route.useRouteContext()
  return (
    <AppShell user={user}>
      <Outlet />
    </AppShell>
  )
}
```

**Step 4: Commit**

```bash
git add frontend-start/src/components/layout/ frontend-start/src/routes/_authed.tsx
git commit -m "feat: add layout components with server-side auth context"
```

---

## Phase 4: Real-Time Events (SSE)

### Task 10: SSE Server Route

**Files:**
- Create: `frontend-start/src/routes/api/events.ts`
- Create: `frontend-start/src/server/events.ts`

**Step 1: Create the event manager**

```typescript
// frontend-start/src/server/events.ts

type EventHandler = (event: string, data: unknown) => void

// In-memory map of connected clients
const clients = new Map<string, EventHandler>()

export function addClient(userId: string, handler: EventHandler): () => void {
  clients.set(userId, handler)
  return () => { clients.delete(userId) }
}

export function sendToUser(userId: string, event: string, data: unknown) {
  const handler = clients.get(userId)
  if (handler) {
    handler(event, data)
  }
}

export function broadcastToAll(event: string, data: unknown) {
  for (const handler of clients.values()) {
    handler(event, data)
  }
}
```

**Step 2: Create the SSE server route**

```typescript
// frontend-start/src/routes/api/events.ts
import { createFileRoute } from '@tanstack/react-router'
import { getSession } from '@/server/session'
import { addClient } from '@/server/events'

export const Route = createFileRoute('/api/events')({
  server: {
    handlers: {
      GET: async ({ request }) => {
        const session = await getSession()
        if (!session.userId) {
          return new Response('Unauthorized', { status: 401 })
        }

        const userId = session.userId
        const stream = new ReadableStream({
          start(controller) {
            const encoder = new TextEncoder()

            const send = (event: string, data: unknown) => {
              controller.enqueue(encoder.encode(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`))
            }

            // Send keepalive every 30s
            const keepalive = setInterval(() => {
              controller.enqueue(encoder.encode(': keepalive\n\n'))
            }, 30_000)

            const removeClient = addClient(userId, send)

            // Clean up on disconnect
            request.signal.addEventListener('abort', () => {
              clearInterval(keepalive)
              removeClient()
            })
          },
        })

        return new Response(stream, {
          headers: {
            'Content-Type': 'text/event-stream',
            'Cache-Control': 'no-cache',
            Connection: 'keep-alive',
          },
        })
      },
    },
  },
})
```

**Step 3: Create internal event receiver for .NET API to push events**

```typescript
// frontend-start/src/routes/api/internal/events.ts
import { createFileRoute } from '@tanstack/react-router'
import { sendToUser, broadcastToAll } from '@/server/events'

export const Route = createFileRoute('/api/internal/events')({
  server: {
    handlers: {
      POST: async ({ request }) => {
        // Only allow internal calls (from Docker network)
        const body = await request.json() as {
          event: string
          data: unknown
          userId?: string
        }

        if (body.userId) {
          sendToUser(body.userId, body.event, body.data)
        } else {
          broadcastToAll(body.event, body.data)
        }

        return Response.json({ ok: true })
      },
    },
  },
})
```

**Step 4: Commit**

```bash
git add frontend-start/src/server/events.ts frontend-start/src/routes/api/
git commit -m "feat: add SSE server route and internal event receiver"
```

---

### Task 11: Client-Side SSE Hook

**Files:**
- Create: `frontend-start/src/hooks/useSSE.ts`

**Step 1: Create the hook**

```typescript
// frontend-start/src/hooks/useSSE.ts
import { useEffect } from 'react'

type SSEEvent = 'NotificationCountUpdated' | 'CriticalVulnerabilityDetected' | 'TaskStatusChanged'

export function useSSE(event: SSEEvent, handler: (data: unknown) => void) {
  useEffect(() => {
    const eventSource = new EventSource('/api/events')

    eventSource.addEventListener(event, (e) => {
      try {
        const data = JSON.parse(e.data)
        handler(data)
      } catch {
        // Ignore parse errors
      }
    })

    return () => {
      eventSource.close()
    }
  }, [event, handler])
}
```

> **Note:** If multiple components use `useSSE`, you may want a singleton `EventSource` connection (similar to the old `SignalRManager`). For now, keep it simple — optimize only if needed.

**Step 2: Commit**

```bash
git add frontend-start/src/hooks/
git commit -m "feat: add useSSE hook for real-time events"
```

---

## Phase 5: Deployment

### Task 12: Dockerfile and Docker Compose

**Files:**
- Create: `frontend-start/Dockerfile`
- Modify: `docker-compose.yml`

**Step 1: Create the Dockerfile**

```dockerfile
# frontend-start/Dockerfile
FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM node:22-alpine AS runtime
WORKDIR /app
COPY --from=build /app/.output .output
COPY --from=build /app/package.json .
ENV NODE_ENV=production
EXPOSE 3000
CMD ["node", ".output/server/index.mjs"]
```

**Step 2: Create .dockerignore**

```
# frontend-start/.dockerignore
node_modules
dist
.output
.env
.env.local
```

**Step 3: Update docker-compose.yml**

Replace the existing `frontend` service and make `api` internal-only:

```yaml
services:
  postgres:
    image: postgres:17
    environment:
      POSTGRES_DB: ${POSTGRES_DB}
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - pgdata:/var/lib/postgresql/data
    ports:
      - "5432:5432"

  api:
    build:
      context: .
      dockerfile: src/Vigil.Api/Dockerfile
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__Vigil: Host=postgres;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}
      AzureAd__ClientId: ${AZURE_AD_CLIENT_ID}
      AzureAd__TenantId: ${AZURE_AD_TENANT_ID}
      AzureAd__Audience: ${AZURE_AD_AUDIENCE}
    depends_on:
      - postgres
    # No ports — internal only, accessed by frontend via Docker network

  worker:
    build:
      context: .
      dockerfile: src/Vigil.Worker/Dockerfile
    environment:
      DOTNET_ENVIRONMENT: Development
      ConnectionStrings__Vigil: Host=postgres;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}
      Defender__ClientId: ${DEFENDER_CLIENT_ID}
      Defender__ClientSecret: ${DEFENDER_CLIENT_SECRET}
      Defender__TenantId: ${DEFENDER_TENANT_ID}
      Defender__ApiBaseUrl: ${DEFENDER_API_BASE_URL}
      Defender__TokenScope: ${DEFENDER_TOKEN_SCOPE}
    depends_on:
      - postgres

  frontend:
    build:
      context: ./frontend-start
      dockerfile: Dockerfile
    environment:
      NODE_ENV: production
      API_BASE_URL: http://api:8080/api
      SESSION_SECRET: ${SESSION_SECRET}
      ENTRA_CLIENT_ID: ${AZURE_AD_CLIENT_ID}
      ENTRA_CLIENT_SECRET: ${ENTRA_CLIENT_SECRET}
      ENTRA_TENANT_ID: ${AZURE_AD_TENANT_ID}
      ENTRA_REDIRECT_URI: ${FRONTEND_ORIGIN}/auth/callback
      ENTRA_SCOPES: ${ENTRA_SCOPES}
      FRONTEND_ORIGIN: ${FRONTEND_ORIGIN}
    depends_on:
      - api
    ports:
      - "3000:3000"

volumes:
  pgdata:
```

**Step 4: Add new env vars to `.env` and `.env.example`**

Add these new variables:
```
SESSION_SECRET=<at-least-32-characters-random-string>
ENTRA_CLIENT_SECRET=<your-entra-client-secret>
ENTRA_SCOPES=openid profile email api://<client-id>/user_impersonate
```

**Step 5: Commit**

```bash
git add frontend-start/Dockerfile frontend-start/.dockerignore docker-compose.yml
git commit -m "feat: add Docker deployment for TanStack Start"
```

---

## Phase 6: Cleanup and .NET API Changes

### Task 13: Update .NET API — Remove CORS, Add Internal Event Push

**Files:**
- Modify: `src/Vigil.Api/Program.cs` — remove CORS configuration
- Modify: `.NET notification service` — add HTTP POST to Start's internal event endpoint

**Step 1: Remove CORS from API**

In `src/Vigil.Api/Program.cs`, remove:
- The `AddCors` service registration
- The `app.UseCors()` middleware call
- The `Frontend:Origin` config reference

**Step 2: Add event push to notification service**

When the .NET API sends a notification (e.g., new critical vulnerability), it should also POST to `http://frontend:3000/api/internal/events`:

```csharp
// Add an IEventPusher interface and implementation
public interface IEventPusher
{
    Task PushAsync(string eventName, object data, string? userId = null, CancellationToken ct = default);
}

public class HttpEventPusher : IEventPusher
{
    private readonly HttpClient _httpClient;

    public HttpEventPusher(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("http://frontend:3000");
    }

    public async Task PushAsync(string eventName, object data, string? userId = null, CancellationToken ct = default)
    {
        var payload = new { @event = eventName, data, userId };
        await _httpClient.PostAsJsonAsync("/api/internal/events", payload, ct);
    }
}
```

Register in DI and call from notification service, SLA check worker, etc.

**Step 3: Commit**

```bash
git add src/Vigil.Api/ src/Vigil.Infrastructure/
git commit -m "feat: remove CORS, add event push to TanStack Start SSE"
```

---

### Task 14: Remove Old Frontend

**Step 1: Remove the old frontend directory**

```bash
rm -rf frontend/
```

**Step 2: Rename frontend-start to frontend**

```bash
mv frontend-start frontend
```

**Step 3: Update docker-compose.yml paths**

Change `context: ./frontend-start` to `context: ./frontend` in `docker-compose.yml`.

**Step 4: Final verification**

```bash
cd frontend && npm run build
docker compose build
docker compose up
```

Verify:
- `http://localhost:3000` loads the login page (SSR)
- Login redirects to Entra and back
- Dashboard loads with SSR data
- All routes work
- Real-time events flow via SSE

**Step 5: Commit**

```bash
git add -A
git commit -m "chore: remove old SPA frontend, rename frontend-start to frontend"
```

---

## Summary

| Phase | Tasks | What it delivers |
|-------|-------|-----------------|
| 1 | 1-2 | Scaffolded TanStack Start project with root route |
| 2 | 3-6 | Server-side Entra ID auth with encrypted session cookies |
| 3 | 7-9 | All BFF server functions, SSR routes, copied UI components |
| 4 | 10-11 | SSE real-time events replacing SignalR |
| 5 | 12 | Docker deployment |
| 6 | 13-14 | .NET API cleanup, old frontend removal |
