# Authenticated Scans Plan 4: Admin UI Workbench

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Authenticated Scans admin workbench page — a tabbed interface for managing scan profiles, scanning tools, connection profiles, and scan runners, with full CRUD operations.

**Architecture:** TanStack Start route at `/admin/authenticated-scans` with 4 tabs driven by `?tab=` search param. Each tab has a DataTable + create/edit dialogs. Server functions call the existing backend controllers (`ScanProfilesController`, `ScanningToolsController`, `ConnectionProfilesController`, `ScanRunnersController`). Zod schemas validate API responses. Role-gated on `CustomerAdmin`.

**Tech Stack:** React 19, TanStack Router + React Query, shadcn/ui (Tabs, DataTable, Dialog, Select, Input), Zod, Sonner toasts

---

## Scope

**In scope:** API schemas, server functions, workbench route with all 4 tabs (data tables + create/edit/delete dialogs), scan runner secret-once modal, manual trigger button on profiles.

**Out of scope (Plan 5):** CodeMirror script editor for tools, version history panel, sources admin tab, asset rule `AssignScanProfile` operation, run report dialog.

---

## File Map

| File | Responsibility |
|------|---------------|
| `frontend/src/api/authenticated-scans.schemas.ts` | Zod schemas for all 4 entity types |
| `frontend/src/api/authenticated-scans.functions.ts` | Server functions (fetch, create, update, delete, trigger) for all entities |
| `frontend/src/routes/_authed/admin/authenticated-scans.tsx` | Route definition + workbench page with tab routing |
| `frontend/src/components/features/admin/scan-profiles/ScanProfilesTab.tsx` | Profiles tab: data table + create/edit dialog |
| `frontend/src/components/features/admin/scan-profiles/ScanProfileDialog.tsx` | Create/edit dialog for scan profiles |
| `frontend/src/components/features/admin/scanning-tools/ScanningToolsTab.tsx` | Tools tab: data table + create dialog (basic, no CodeMirror yet) |
| `frontend/src/components/features/admin/connection-profiles/ConnectionProfilesTab.tsx` | Connections tab: data table + create/edit dialog |
| `frontend/src/components/features/admin/connection-profiles/ConnectionProfileDialog.tsx` | Create/edit dialog for connection profiles |
| `frontend/src/components/features/admin/scan-runners/ScanRunnersTab.tsx` | Runners tab: data table + create dialog + secret modal |
| `frontend/src/components/features/admin/scan-runners/RunnerSecretModal.tsx` | One-time secret display modal with copy + runner.yaml snippet |

---

## Task 1: Zod schemas for all authenticated scan entities

**Files:**
- Create: `frontend/src/api/authenticated-scans.schemas.ts`

- [ ] **Step 1: Create schemas file**

```typescript
import { z } from 'zod'
import { isoDateTimeSchema, nullableIsoDateTimeSchema } from './common.schemas'
import { pagedResponseMetaSchema } from './pagination.schemas'

// --- Scan Profiles ---

export const scanProfileSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  name: z.string(),
  description: z.string(),
  cronSchedule: z.string(),
  connectionProfileId: z.string().uuid(),
  scanRunnerId: z.string().uuid(),
  enabled: z.boolean(),
  manualRequestedAt: nullableIsoDateTimeSchema,
  lastRunStartedAt: nullableIsoDateTimeSchema,
  createdAt: isoDateTimeSchema,
  updatedAt: isoDateTimeSchema,
  toolIds: z.array(z.string().uuid()),
})

export const pagedScanProfilesSchema = pagedResponseMetaSchema.extend({
  items: z.array(scanProfileSchema),
})

export type ScanProfile = z.infer<typeof scanProfileSchema>
export type PagedScanProfiles = z.infer<typeof pagedScanProfilesSchema>

// --- Scanning Tools ---

export const scanningToolSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  name: z.string(),
  description: z.string(),
  scriptType: z.string(),
  interpreterPath: z.string(),
  timeoutSeconds: z.number(),
  outputModel: z.string(),
  currentVersionId: z.string().uuid().nullable(),
  createdAt: isoDateTimeSchema,
  updatedAt: isoDateTimeSchema,
})

export const pagedScanningToolsSchema = pagedResponseMetaSchema.extend({
  items: z.array(scanningToolSchema),
})

export type ScanningTool = z.infer<typeof scanningToolSchema>
export type PagedScanningTools = z.infer<typeof pagedScanningToolsSchema>

export const scanningToolVersionSchema = z.object({
  id: z.string().uuid(),
  scanningToolId: z.string().uuid(),
  versionNumber: z.number(),
  scriptContent: z.string(),
  editedByUserId: z.string().uuid(),
  editedAt: isoDateTimeSchema,
})

export type ScanningToolVersion = z.infer<typeof scanningToolVersionSchema>

// --- Connection Profiles ---

export const connectionProfileSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  name: z.string(),
  description: z.string(),
  sshHost: z.string(),
  sshPort: z.number(),
  sshUsername: z.string(),
  authMethod: z.string(),
  hostKeyFingerprint: z.string().nullable(),
  createdAt: isoDateTimeSchema,
  updatedAt: isoDateTimeSchema,
})

export const pagedConnectionProfilesSchema = pagedResponseMetaSchema.extend({
  items: z.array(connectionProfileSchema),
})

export type ConnectionProfile = z.infer<typeof connectionProfileSchema>
export type PagedConnectionProfiles = z.infer<typeof pagedConnectionProfilesSchema>

// --- Scan Runners ---

export const scanRunnerSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  name: z.string(),
  description: z.string(),
  lastSeenAt: nullableIsoDateTimeSchema,
  version: z.string(),
  enabled: z.boolean(),
  createdAt: isoDateTimeSchema,
  updatedAt: isoDateTimeSchema,
})

export const pagedScanRunnersSchema = pagedResponseMetaSchema.extend({
  items: z.array(scanRunnerSchema),
})

export const createScanRunnerResponseSchema = z.object({
  runner: scanRunnerSchema,
  bearerSecret: z.string(),
})

export const rotateSecretResponseSchema = z.object({
  bearerSecret: z.string(),
})

export const triggerRunResponseSchema = z.object({
  runId: z.string().uuid(),
})

export type ScanRunner = z.infer<typeof scanRunnerSchema>
export type PagedScanRunners = z.infer<typeof pagedScanRunnersSchema>
export type CreateScanRunnerResponse = z.infer<typeof createScanRunnerResponseSchema>
export type RotateSecretResponse = z.infer<typeof rotateSecretResponseSchema>
```

- [ ] **Step 2: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1 | head -20`
Expected: No errors related to this file.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/authenticated-scans.schemas.ts
git commit -m "feat: add Zod schemas for authenticated scan entities"
```

---

## Task 2: Server functions for all CRUD operations

**Files:**
- Create: `frontend/src/api/authenticated-scans.functions.ts`

- [ ] **Step 1: Create server functions**

```typescript
import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiDelete, apiGet, apiPost, apiPut } from '@/server/api'
import { buildFilterParams } from './utils'
import {
  createScanRunnerResponseSchema,
  pagedConnectionProfilesSchema,
  pagedScanProfilesSchema,
  pagedScanRunnersSchema,
  pagedScanningToolsSchema,
  rotateSecretResponseSchema,
  scanProfileSchema,
  triggerRunResponseSchema,
} from './authenticated-scans.schemas'

// ─── Scan Profiles ───

export const fetchScanProfiles = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ page: z.number().optional(), pageSize: z.number().optional() }))
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    return pagedScanProfilesSchema.parse(await apiGet(`/scan-profiles?${params}`, context))
  })

export const createScanProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      name: z.string().min(1),
      description: z.string(),
      cronSchedule: z.string(),
      connectionProfileId: z.string().uuid(),
      scanRunnerId: z.string().uuid(),
      enabled: z.boolean(),
      toolIds: z.array(z.string().uuid()),
    }),
  )
  .handler(async ({ context, data }) => {
    return scanProfileSchema.parse(await apiPost('/scan-profiles', context, data))
  })

export const updateScanProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      id: z.string().uuid(),
      name: z.string().min(1),
      description: z.string(),
      cronSchedule: z.string(),
      connectionProfileId: z.string().uuid(),
      scanRunnerId: z.string().uuid(),
      enabled: z.boolean(),
      toolIds: z.array(z.string().uuid()),
    }),
  )
  .handler(async ({ context, data: { id, ...body } }) => {
    return scanProfileSchema.parse(await apiPut(`/scan-profiles/${id}`, context, body))
  })

export const deleteScanProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    await apiDelete(`/scan-profiles/${id}`, context)
  })

export const triggerScanRun = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    return triggerRunResponseSchema.parse(
      await apiPost(`/scan-profiles/${id}/trigger`, context, { triggerKind: 'manual' }),
    )
  })

// ─── Scanning Tools ───

export const fetchScanningTools = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ page: z.number().optional(), pageSize: z.number().optional() }))
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    return pagedScanningToolsSchema.parse(await apiGet(`/scanning-tools?${params}`, context))
  })

export const createScanningTool = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      name: z.string().min(1),
      description: z.string(),
      scriptType: z.enum(['python', 'bash', 'powershell']),
      interpreterPath: z.string().min(1),
      timeoutSeconds: z.number().min(5).max(3600),
      initialScript: z.string(),
    }),
  )
  .handler(async ({ context, data }) => {
    return await apiPost('/scanning-tools', context, data)
  })

export const updateScanningTool = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      id: z.string().uuid(),
      name: z.string().min(1),
      description: z.string(),
      scriptType: z.enum(['python', 'bash', 'powershell']),
      interpreterPath: z.string().min(1),
      timeoutSeconds: z.number().min(5).max(3600),
    }),
  )
  .handler(async ({ context, data: { id, ...body } }) => {
    await apiPut(`/scanning-tools/${id}`, context, body)
  })

export const deleteScanningTool = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    await apiDelete(`/scanning-tools/${id}`, context)
  })

// ─── Connection Profiles ───

export const fetchConnectionProfiles = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ page: z.number().optional(), pageSize: z.number().optional() }))
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    return pagedConnectionProfilesSchema.parse(await apiGet(`/connection-profiles?${params}`, context))
  })

export const createConnectionProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      name: z.string().min(1),
      description: z.string(),
      sshHost: z.string().min(1),
      sshPort: z.number().min(1).max(65535),
      sshUsername: z.string().min(1),
      authMethod: z.enum(['password', 'privateKey']),
      password: z.string().optional(),
      privateKey: z.string().optional(),
      passphrase: z.string().optional(),
      hostKeyFingerprint: z.string().optional(),
    }),
  )
  .handler(async ({ context, data }) => {
    return await apiPost('/connection-profiles', context, data)
  })

export const updateConnectionProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      id: z.string().uuid(),
      name: z.string().min(1),
      description: z.string(),
      sshHost: z.string().min(1),
      sshPort: z.number().min(1).max(65535),
      sshUsername: z.string().min(1),
      authMethod: z.enum(['password', 'privateKey']),
      password: z.string().optional(),
      privateKey: z.string().optional(),
      passphrase: z.string().optional(),
      hostKeyFingerprint: z.string().optional(),
    }),
  )
  .handler(async ({ context, data: { id, ...body } }) => {
    await apiPut(`/connection-profiles/${id}`, context, body)
  })

export const deleteConnectionProfile = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    await apiDelete(`/connection-profiles/${id}`, context)
  })

// ─── Scan Runners ───

export const fetchScanRunners = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ page: z.number().optional(), pageSize: z.number().optional() }))
  .handler(async ({ context, data: filters }) => {
    const params = buildFilterParams(filters)
    return pagedScanRunnersSchema.parse(await apiGet(`/scan-runners?${params}`, context))
  })

export const createScanRunner = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ name: z.string().min(1), description: z.string() }))
  .handler(async ({ context, data }) => {
    return createScanRunnerResponseSchema.parse(await apiPost('/scan-runners', context, data))
  })

export const updateScanRunner = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(
    z.object({
      id: z.string().uuid(),
      name: z.string().min(1),
      description: z.string(),
      enabled: z.boolean(),
    }),
  )
  .handler(async ({ context, data: { id, ...body } }) => {
    await apiPut(`/scan-runners/${id}`, context, body)
  })

export const rotateScanRunnerSecret = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    return rotateSecretResponseSchema.parse(await apiPost(`/scan-runners/${id}/rotate-secret`, context, {}))
  })

export const deleteScanRunner = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    await apiDelete(`/scan-runners/${id}`, context)
  })
```

- [ ] **Step 2: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1 | head -20`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/authenticated-scans.functions.ts
git commit -m "feat: add server functions for authenticated scan CRUD operations"
```

---

## Task 3: Workbench route with tab skeleton

**Files:**
- Create: `frontend/src/routes/_authed/admin/authenticated-scans.tsx`

- [ ] **Step 1: Create the route file**

```typescript
import { createFileRoute, redirect } from '@tanstack/react-router'
import { z } from 'zod'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
  fetchConnectionProfiles,
  fetchScanProfiles,
  fetchScanRunners,
  fetchScanningTools,
} from '@/api/authenticated-scans.functions'
import { baseListSearchSchema } from '@/routes/-list-search'

const tabValues = ['profiles', 'tools', 'connections', 'runners'] as const

export const Route = createFileRoute('/_authed/admin/authenticated-scans')({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes('CustomerAdmin') && !activeRoles.includes('GlobalAdmin')) {
      throw redirect({ to: '/admin' })
    }
  },
  validateSearch: baseListSearchSchema.extend({
    tab: z.enum(tabValues).optional(),
  }),
  loaderDeps: ({ search }) => search,
  loader: async ({ deps }) => {
    const tab = deps.tab ?? 'profiles'
    const paging = { page: deps.page, pageSize: deps.pageSize }

    // Only fetch data for the active tab
    if (tab === 'profiles') {
      const [profiles, tools, connections, runners] = await Promise.all([
        fetchScanProfiles({ data: paging }),
        fetchScanningTools({ data: { pageSize: 100 } }),
        fetchConnectionProfiles({ data: { pageSize: 100 } }),
        fetchScanRunners({ data: { pageSize: 100 } }),
      ])
      return { profiles, tools, connections, runners, tab }
    }
    if (tab === 'tools') {
      return { tools: await fetchScanningTools({ data: paging }), tab }
    }
    if (tab === 'connections') {
      return { connections: await fetchConnectionProfiles({ data: paging }), tab }
    }
    return { runners: await fetchScanRunners({ data: paging }), tab }
  },
  component: AuthenticatedScansWorkbench,
})

function AuthenticatedScansWorkbench() {
  const navigate = Route.useNavigate()
  const search = Route.useSearch()
  const loaderData = Route.useLoaderData()
  const activeTab = search.tab ?? 'profiles'

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Authenticated Scans</h1>
        <p className="text-muted-foreground text-sm">
          Manage scan profiles, tools, connections, and runners for on-prem host scanning.
        </p>
      </div>

      <Tabs
        value={activeTab}
        onValueChange={(value) =>
          navigate({ search: { tab: value as (typeof tabValues)[number], page: 1, pageSize: search.pageSize } })
        }
      >
        <TabsList className="h-10 w-full justify-start rounded-xl bg-muted/50 p-1">
          <TabsTrigger value="profiles">Scan Profiles</TabsTrigger>
          <TabsTrigger value="tools">Scanning Tools</TabsTrigger>
          <TabsTrigger value="connections">Connections</TabsTrigger>
          <TabsTrigger value="runners">Runners</TabsTrigger>
        </TabsList>

        <TabsContent value="profiles" className="space-y-4 pt-1">
          <p className="text-muted-foreground text-sm">Scan profiles tab — loaded in next task.</p>
        </TabsContent>

        <TabsContent value="tools" className="space-y-4 pt-1">
          <p className="text-muted-foreground text-sm">Scanning tools tab — loaded in next task.</p>
        </TabsContent>

        <TabsContent value="connections" className="space-y-4 pt-1">
          <p className="text-muted-foreground text-sm">Connection profiles tab — loaded in next task.</p>
        </TabsContent>

        <TabsContent value="runners" className="space-y-4 pt-1">
          <p className="text-muted-foreground text-sm">Scan runners tab — loaded in next task.</p>
        </TabsContent>
      </Tabs>
    </div>
  )
}
```

- [ ] **Step 2: Verify dev server compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1 | head -20`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/routes/_authed/admin/authenticated-scans.tsx
git commit -m "feat: add authenticated scans workbench route with tab skeleton"
```

---

## Task 4: Scan Runners tab (simplest entity — build the pattern)

**Files:**
- Create: `frontend/src/components/features/admin/scan-runners/ScanRunnersTab.tsx`
- Create: `frontend/src/components/features/admin/scan-runners/RunnerSecretModal.tsx`
- Modify: `frontend/src/routes/_authed/admin/authenticated-scans.tsx`

This is the simplest CRUD entity (name, description, enabled). We build it first to establish the pattern for the other tabs.

- [ ] **Step 1: Create RunnerSecretModal**

```typescript
import { useState } from 'react'
import { Check, Copy } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'

type Props = {
  open: boolean
  onOpenChange: (open: boolean) => void
  runnerName: string
  runnerId: string
  bearerSecret: string
  centralUrl?: string
}

export function RunnerSecretModal({ open, onOpenChange, runnerName, runnerId, bearerSecret, centralUrl }: Props) {
  const [copied, setCopied] = useState<string | null>(null)

  const yamlSnippet = `# runner.yaml for ${runnerName}
centralUrl: "${centralUrl ?? 'https://your-patchhound-instance.com'}"
bearerToken: "${bearerSecret}"
maxConcurrentJobs: 10
pollIntervalSeconds: 10
heartbeatIntervalSeconds: 30`

  const copyToClipboard = async (text: string, label: string) => {
    await navigator.clipboard.writeText(text)
    setCopied(label)
    setTimeout(() => setCopied(null), 2000)
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Runner Created: {runnerName}</DialogTitle>
          <DialogDescription>
            Save the bearer secret below — it will not be shown again.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div>
            <label className="text-sm font-medium">Runner ID</label>
            <div className="mt-1 flex items-center gap-2">
              <code className="bg-muted rounded px-2 py-1 text-sm flex-1 break-all">{runnerId}</code>
              <Button size="icon" variant="ghost" onClick={() => copyToClipboard(runnerId, 'id')}>
                {copied === 'id' ? <Check className="h-4 w-4" /> : <Copy className="h-4 w-4" />}
              </Button>
            </div>
          </div>

          <div>
            <label className="text-sm font-medium">Bearer Secret</label>
            <div className="mt-1 flex items-center gap-2">
              <code className="bg-muted rounded px-2 py-1 text-sm flex-1 break-all font-mono">{bearerSecret}</code>
              <Button size="icon" variant="ghost" onClick={() => copyToClipboard(bearerSecret, 'secret')}>
                {copied === 'secret' ? <Check className="h-4 w-4" /> : <Copy className="h-4 w-4" />}
              </Button>
            </div>
          </div>

          <div>
            <div className="flex items-center justify-between">
              <label className="text-sm font-medium">runner.yaml</label>
              <Button size="sm" variant="ghost" onClick={() => copyToClipboard(yamlSnippet, 'yaml')}>
                {copied === 'yaml' ? <Check className="mr-1 h-3 w-3" /> : <Copy className="mr-1 h-3 w-3" />}
                {copied === 'yaml' ? 'Copied' : 'Copy'}
              </Button>
            </div>
            <pre className="bg-muted mt-1 rounded p-3 text-xs overflow-x-auto whitespace-pre">{yamlSnippet}</pre>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
```

- [ ] **Step 2: Create ScanRunnersTab**

```typescript
import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { toast } from 'sonner'
import { formatDistanceToNow } from 'date-fns'
import { Plus, RotateCw, Trash2 } from 'lucide-react'
import {
  createScanRunner,
  deleteScanRunner,
  fetchScanRunners,
  rotateScanRunnerSecret,
  updateScanRunner,
} from '@/api/authenticated-scans.functions'
import type { PagedScanRunners, ScanRunner } from '@/api/authenticated-scans.schemas'
import { RunnerSecretModal } from './RunnerSecretModal'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { PaginationControls } from '@/components/ui/pagination-controls'
import { Textarea } from '@/components/ui/textarea'

type Props = {
  initialData: PagedScanRunners
  page: number
  pageSize: number
  onPageChange: (page: number) => void
}

export function ScanRunnersTab({ initialData, page, pageSize, onPageChange }: Props) {
  const queryClient = useQueryClient()
  const [createOpen, setCreateOpen] = useState(false)
  const [secretModal, setSecretModal] = useState<{
    open: boolean
    runnerName: string
    runnerId: string
    bearerSecret: string
  }>({ open: false, runnerName: '', runnerId: '', bearerSecret: '' })
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')

  const query = useQuery({
    queryKey: ['scan-runners', page, pageSize],
    queryFn: () => fetchScanRunners({ data: { page, pageSize } }),
    initialData,
  })

  const createMutation = useMutation({
    mutationFn: createScanRunner,
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ['scan-runners'] })
      setCreateOpen(false)
      setName('')
      setDescription('')
      setSecretModal({
        open: true,
        runnerName: result.runner.name,
        runnerId: result.runner.id,
        bearerSecret: result.bearerSecret,
      })
    },
    onError: () => toast.error('Failed to create runner'),
  })

  const deleteMutation = useMutation({
    mutationFn: deleteScanRunner,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scan-runners'] })
      toast.success('Runner deleted')
    },
    onError: () => toast.error('Failed to delete runner'),
  })

  const rotateMutation = useMutation({
    mutationFn: rotateScanRunnerSecret,
    onSuccess: (result, variables) => {
      const runner = query.data?.items.find((r) => r.id === variables.data.id)
      setSecretModal({
        open: true,
        runnerName: runner?.name ?? 'Runner',
        runnerId: variables.data.id,
        bearerSecret: result.bearerSecret,
      })
    },
    onError: () => toast.error('Failed to rotate secret'),
  })

  const isOnline = (runner: ScanRunner) => {
    if (!runner.lastSeenAt) return false
    const diff = Date.now() - new Date(runner.lastSeenAt).getTime()
    return diff < 2 * 60 * 1000
  }

  const columns: ColumnDef<ScanRunner>[] = [
    { accessorKey: 'name', header: 'Name' },
    {
      accessorKey: 'lastSeenAt',
      header: 'Last Seen',
      cell: ({ row }) => {
        const val = row.original.lastSeenAt
        if (!val) return <span className="text-muted-foreground">Never</span>
        return formatDistanceToNow(new Date(val), { addSuffix: true })
      },
    },
    { accessorKey: 'version', header: 'Version' },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) =>
        isOnline(row.original) ? (
          <Badge variant="default" className="bg-green-600">Online</Badge>
        ) : (
          <Badge variant="secondary">Offline</Badge>
        ),
    },
    {
      id: 'enabled',
      header: 'Enabled',
      cell: ({ row }) => (row.original.enabled ? 'Yes' : 'No'),
    },
    {
      id: 'actions',
      cell: ({ row }) => (
        <div className="flex gap-1">
          <Button
            size="sm"
            variant="ghost"
            onClick={() => rotateMutation.mutate({ data: { id: row.original.id } })}
            disabled={rotateMutation.isPending}
          >
            <RotateCw className="mr-1 h-3 w-3" />
            Rotate Secret
          </Button>
          <Button
            size="sm"
            variant="ghost"
            className="text-destructive"
            onClick={() => {
              if (confirm(`Delete runner "${row.original.name}"?`)) {
                deleteMutation.mutate({ data: { id: row.original.id } })
              }
            }}
          >
            <Trash2 className="h-3 w-3" />
          </Button>
        </div>
      ),
    },
  ]

  return (
    <>
      <Card>
        <CardHeader className="flex flex-row items-center justify-between">
          <CardTitle>Scan Runners</CardTitle>
          <Button size="sm" onClick={() => setCreateOpen(true)}>
            <Plus className="mr-1 h-4 w-4" />
            New Runner
          </Button>
        </CardHeader>
        <CardContent>
          <DataTable columns={columns} data={query.data?.items ?? []} />
          <PaginationControls
            page={page}
            pageSize={pageSize}
            totalCount={query.data?.totalCount ?? 0}
            onPageChange={onPageChange}
          />
        </CardContent>
      </Card>

      {/* Create dialog */}
      <Dialog open={createOpen} onOpenChange={setCreateOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>New Scan Runner</DialogTitle>
            <DialogDescription>
              Create a runner identity. A bearer secret will be shown once after creation.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4">
            <div>
              <Label htmlFor="runner-name">Name</Label>
              <Input id="runner-name" value={name} onChange={(e) => setName(e.target.value)} placeholder="prod-scanner-01" />
            </div>
            <div>
              <Label htmlFor="runner-desc">Description</Label>
              <Textarea id="runner-desc" value={description} onChange={(e) => setDescription(e.target.value)} placeholder="On-prem runner in datacenter A" />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setCreateOpen(false)}>Cancel</Button>
            <Button
              onClick={() => createMutation.mutate({ data: { name, description } })}
              disabled={!name || createMutation.isPending}
            >
              Create Runner
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Secret modal */}
      <RunnerSecretModal
        open={secretModal.open}
        onOpenChange={(open) => setSecretModal((s) => ({ ...s, open }))}
        runnerName={secretModal.runnerName}
        runnerId={secretModal.runnerId}
        bearerSecret={secretModal.bearerSecret}
      />
    </>
  )
}
```

- [ ] **Step 3: Wire into workbench route**

In `frontend/src/routes/_authed/admin/authenticated-scans.tsx`, replace the runners `TabsContent` placeholder:

Import the component at the top:
```typescript
import { ScanRunnersTab } from '@/components/features/admin/scan-runners/ScanRunnersTab'
```

Replace the runners TabsContent:
```typescript
        <TabsContent value="runners" className="space-y-4 pt-1">
          {'runners' in loaderData && loaderData.runners && (
            <ScanRunnersTab
              initialData={loaderData.runners}
              page={search.page}
              pageSize={search.pageSize}
              onPageChange={(p) => navigate({ search: { ...search, page: p } })}
            />
          )}
        </TabsContent>
```

- [ ] **Step 4: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1 | head -20`
Expected: No errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/features/admin/scan-runners/ \
  frontend/src/routes/_authed/admin/authenticated-scans.tsx
git commit -m "feat: add Scan Runners tab with create dialog and secret modal"
```

---

## Task 5: Connection Profiles tab

**Files:**
- Create: `frontend/src/components/features/admin/connection-profiles/ConnectionProfilesTab.tsx`
- Create: `frontend/src/components/features/admin/connection-profiles/ConnectionProfileDialog.tsx`
- Modify: `frontend/src/routes/_authed/admin/authenticated-scans.tsx`

- [ ] **Step 1: Create ConnectionProfileDialog**

```typescript
import { useState, useEffect } from 'react'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import type { ConnectionProfile } from '@/api/authenticated-scans.schemas'

type Props = {
  open: boolean
  onOpenChange: (open: boolean) => void
  profile?: ConnectionProfile | null
  onSubmit: (data: {
    name: string
    description: string
    sshHost: string
    sshPort: number
    sshUsername: string
    authMethod: 'password' | 'privateKey'
    password?: string
    privateKey?: string
    passphrase?: string
    hostKeyFingerprint?: string
  }) => void
  isPending: boolean
}

export function ConnectionProfileDialog({ open, onOpenChange, profile, onSubmit, isPending }: Props) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [sshHost, setSshHost] = useState('')
  const [sshPort, setSshPort] = useState(22)
  const [sshUsername, setSshUsername] = useState('')
  const [authMethod, setAuthMethod] = useState<'password' | 'privateKey'>('password')
  const [password, setPassword] = useState('')
  const [privateKey, setPrivateKey] = useState('')
  const [passphrase, setPassphrase] = useState('')
  const [hostKeyFingerprint, setHostKeyFingerprint] = useState('')

  useEffect(() => {
    if (profile) {
      setName(profile.name)
      setDescription(profile.description)
      setSshHost(profile.sshHost)
      setSshPort(profile.sshPort)
      setSshUsername(profile.sshUsername)
      setAuthMethod(profile.authMethod as 'password' | 'privateKey')
      setHostKeyFingerprint(profile.hostKeyFingerprint ?? '')
    } else {
      setName('')
      setDescription('')
      setSshHost('')
      setSshPort(22)
      setSshUsername('')
      setAuthMethod('password')
      setHostKeyFingerprint('')
    }
    setPassword('')
    setPrivateKey('')
    setPassphrase('')
  }, [profile, open])

  const isEdit = Boolean(profile)
  const canSubmit = name && sshHost && sshUsername && (isEdit || (authMethod === 'password' ? password : privateKey))

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>{isEdit ? 'Edit' : 'New'} Connection Profile</DialogTitle>
          <DialogDescription>SSH connection details for target hosts.</DialogDescription>
        </DialogHeader>
        <div className="space-y-4 max-h-[60vh] overflow-y-auto pr-1">
          <div>
            <Label>Name</Label>
            <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="prod-linux-servers" />
          </div>
          <div>
            <Label>Description</Label>
            <Textarea value={description} onChange={(e) => setDescription(e.target.value)} />
          </div>
          <div className="grid grid-cols-3 gap-3">
            <div className="col-span-2">
              <Label>SSH Host</Label>
              <Input value={sshHost} onChange={(e) => setSshHost(e.target.value)} placeholder="10.0.1.50" />
            </div>
            <div>
              <Label>Port</Label>
              <Input type="number" value={sshPort} onChange={(e) => setSshPort(Number(e.target.value))} />
            </div>
          </div>
          <div>
            <Label>Username</Label>
            <Input value={sshUsername} onChange={(e) => setSshUsername(e.target.value)} placeholder="scanner" />
          </div>
          <div>
            <Label>Auth Method</Label>
            <div className="mt-1 flex gap-4">
              <label className="flex items-center gap-2 text-sm">
                <input type="radio" checked={authMethod === 'password'} onChange={() => setAuthMethod('password')} />
                Password
              </label>
              <label className="flex items-center gap-2 text-sm">
                <input type="radio" checked={authMethod === 'privateKey'} onChange={() => setAuthMethod('privateKey')} />
                Private Key
              </label>
            </div>
          </div>
          {authMethod === 'password' ? (
            <div>
              <Label>{isEdit ? 'New Password (leave blank to keep)' : 'Password'}</Label>
              <Input type="password" value={password} onChange={(e) => setPassword(e.target.value)} />
            </div>
          ) : (
            <>
              <div>
                <Label>{isEdit ? 'New Private Key (leave blank to keep)' : 'Private Key'}</Label>
                <Textarea value={privateKey} onChange={(e) => setPrivateKey(e.target.value)} rows={4} className="font-mono text-xs" placeholder="-----BEGIN RSA PRIVATE KEY-----" />
              </div>
              <div>
                <Label>Passphrase (optional)</Label>
                <Input type="password" value={passphrase} onChange={(e) => setPassphrase(e.target.value)} />
              </div>
            </>
          )}
          <div>
            <Label>Host Key Fingerprint (optional)</Label>
            <Input value={hostKeyFingerprint} onChange={(e) => setHostKeyFingerprint(e.target.value)} placeholder="SHA256:..." />
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
          <Button
            onClick={() =>
              onSubmit({
                name,
                description,
                sshHost,
                sshPort,
                sshUsername,
                authMethod,
                ...(password ? { password } : {}),
                ...(privateKey ? { privateKey } : {}),
                ...(passphrase ? { passphrase } : {}),
                ...(hostKeyFingerprint ? { hostKeyFingerprint } : {}),
              })
            }
            disabled={!canSubmit || isPending}
          >
            {isEdit ? 'Save' : 'Create'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
```

- [ ] **Step 2: Create ConnectionProfilesTab**

```typescript
import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { toast } from 'sonner'
import { PenSquare, Plus, Trash2 } from 'lucide-react'
import {
  createConnectionProfile,
  deleteConnectionProfile,
  fetchConnectionProfiles,
  updateConnectionProfile,
} from '@/api/authenticated-scans.functions'
import type { ConnectionProfile, PagedConnectionProfiles } from '@/api/authenticated-scans.schemas'
import { ConnectionProfileDialog } from './ConnectionProfileDialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import { PaginationControls } from '@/components/ui/pagination-controls'

type Props = {
  initialData: PagedConnectionProfiles
  page: number
  pageSize: number
  onPageChange: (page: number) => void
}

export function ConnectionProfilesTab({ initialData, page, pageSize, onPageChange }: Props) {
  const queryClient = useQueryClient()
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editing, setEditing] = useState<ConnectionProfile | null>(null)

  const query = useQuery({
    queryKey: ['connection-profiles', page, pageSize],
    queryFn: () => fetchConnectionProfiles({ data: { page, pageSize } }),
    initialData,
  })

  const createMutation = useMutation({
    mutationFn: createConnectionProfile,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['connection-profiles'] })
      setDialogOpen(false)
      toast.success('Connection profile created')
    },
    onError: () => toast.error('Failed to create connection profile'),
  })

  const updateMutation = useMutation({
    mutationFn: updateConnectionProfile,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['connection-profiles'] })
      setDialogOpen(false)
      setEditing(null)
      toast.success('Connection profile updated')
    },
    onError: () => toast.error('Failed to update connection profile'),
  })

  const deleteMutation = useMutation({
    mutationFn: deleteConnectionProfile,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['connection-profiles'] })
      toast.success('Connection profile deleted')
    },
    onError: () => toast.error('Failed to delete connection profile'),
  })

  const columns: ColumnDef<ConnectionProfile>[] = [
    { accessorKey: 'name', header: 'Name' },
    {
      id: 'host',
      header: 'Host',
      cell: ({ row }) => `${row.original.sshHost}:${row.original.sshPort}`,
    },
    { accessorKey: 'sshUsername', header: 'Username' },
    {
      accessorKey: 'authMethod',
      header: 'Auth',
      cell: ({ row }) => (
        <Badge variant="outline">{row.original.authMethod === 'privateKey' ? 'Key' : 'Password'}</Badge>
      ),
    },
    {
      id: 'actions',
      cell: ({ row }) => (
        <div className="flex gap-1">
          <Button
            size="sm"
            variant="ghost"
            onClick={() => {
              setEditing(row.original)
              setDialogOpen(true)
            }}
          >
            <PenSquare className="h-3 w-3" />
          </Button>
          <Button
            size="sm"
            variant="ghost"
            className="text-destructive"
            onClick={() => {
              if (confirm(`Delete "${row.original.name}"?`)) {
                deleteMutation.mutate({ data: { id: row.original.id } })
              }
            }}
          >
            <Trash2 className="h-3 w-3" />
          </Button>
        </div>
      ),
    },
  ]

  return (
    <>
      <Card>
        <CardHeader className="flex flex-row items-center justify-between">
          <CardTitle>Connection Profiles</CardTitle>
          <Button
            size="sm"
            onClick={() => {
              setEditing(null)
              setDialogOpen(true)
            }}
          >
            <Plus className="mr-1 h-4 w-4" />
            New Connection
          </Button>
        </CardHeader>
        <CardContent>
          <DataTable columns={columns} data={query.data?.items ?? []} />
          <PaginationControls
            page={page}
            pageSize={pageSize}
            totalCount={query.data?.totalCount ?? 0}
            onPageChange={onPageChange}
          />
        </CardContent>
      </Card>

      <ConnectionProfileDialog
        open={dialogOpen}
        onOpenChange={(open) => {
          setDialogOpen(open)
          if (!open) setEditing(null)
        }}
        profile={editing}
        isPending={createMutation.isPending || updateMutation.isPending}
        onSubmit={(data) => {
          if (editing) {
            updateMutation.mutate({ data: { id: editing.id, ...data } })
          } else {
            createMutation.mutate({ data })
          }
        }}
      />
    </>
  )
}
```

- [ ] **Step 3: Wire into workbench route**

In `authenticated-scans.tsx`, add import:
```typescript
import { ConnectionProfilesTab } from '@/components/features/admin/connection-profiles/ConnectionProfilesTab'
```

Replace connections TabsContent:
```typescript
        <TabsContent value="connections" className="space-y-4 pt-1">
          {'connections' in loaderData && loaderData.connections && (
            <ConnectionProfilesTab
              initialData={loaderData.connections}
              page={search.page}
              pageSize={search.pageSize}
              onPageChange={(p) => navigate({ search: { ...search, page: p } })}
            />
          )}
        </TabsContent>
```

- [ ] **Step 4: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1 | head -20`

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/features/admin/connection-profiles/ \
  frontend/src/routes/_authed/admin/authenticated-scans.tsx
git commit -m "feat: add Connection Profiles tab with create/edit dialog"
```

---

## Task 6: Scanning Tools tab (basic — no CodeMirror yet)

**Files:**
- Create: `frontend/src/components/features/admin/scanning-tools/ScanningToolsTab.tsx`
- Modify: `frontend/src/routes/_authed/admin/authenticated-scans.tsx`

- [ ] **Step 1: Create ScanningToolsTab**

```typescript
import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { toast } from 'sonner'
import { Plus, Trash2 } from 'lucide-react'
import {
  createScanningTool,
  deleteScanningTool,
  fetchScanningTools,
} from '@/api/authenticated-scans.functions'
import type { PagedScanningTools, ScanningTool } from '@/api/authenticated-scans.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { PaginationControls } from '@/components/ui/pagination-controls'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'

type Props = {
  initialData: PagedScanningTools
  page: number
  pageSize: number
  onPageChange: (page: number) => void
}

const scriptTypes = ['python', 'bash', 'powershell'] as const
const defaultInterpreters: Record<string, string> = {
  python: '/usr/bin/python3',
  bash: '/bin/bash',
  powershell: '/usr/bin/pwsh',
}

export function ScanningToolsTab({ initialData, page, pageSize, onPageChange }: Props) {
  const queryClient = useQueryClient()
  const [createOpen, setCreateOpen] = useState(false)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [scriptType, setScriptType] = useState<(typeof scriptTypes)[number]>('python')
  const [interpreterPath, setInterpreterPath] = useState(defaultInterpreters.python)
  const [timeoutSeconds, setTimeoutSeconds] = useState(300)
  const [initialScript, setInitialScript] = useState('')

  const query = useQuery({
    queryKey: ['scanning-tools', page, pageSize],
    queryFn: () => fetchScanningTools({ data: { page, pageSize } }),
    initialData,
  })

  const createMutation = useMutation({
    mutationFn: createScanningTool,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scanning-tools'] })
      setCreateOpen(false)
      resetForm()
      toast.success('Scanning tool created')
    },
    onError: () => toast.error('Failed to create scanning tool'),
  })

  const deleteMutation = useMutation({
    mutationFn: deleteScanningTool,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scanning-tools'] })
      toast.success('Scanning tool deleted')
    },
    onError: () => toast.error('Failed to delete scanning tool'),
  })

  const resetForm = () => {
    setName('')
    setDescription('')
    setScriptType('python')
    setInterpreterPath(defaultInterpreters.python)
    setTimeoutSeconds(300)
    setInitialScript('')
  }

  const columns: ColumnDef<ScanningTool>[] = [
    { accessorKey: 'name', header: 'Name' },
    {
      accessorKey: 'scriptType',
      header: 'Type',
      cell: ({ row }) => <Badge variant="outline">{row.original.scriptType}</Badge>,
    },
    {
      accessorKey: 'outputModel',
      header: 'Output',
      cell: ({ row }) => <Badge variant="secondary">{row.original.outputModel}</Badge>,
    },
    {
      accessorKey: 'timeoutSeconds',
      header: 'Timeout',
      cell: ({ row }) => `${row.original.timeoutSeconds}s`,
    },
    {
      id: 'actions',
      cell: ({ row }) => (
        <Button
          size="sm"
          variant="ghost"
          className="text-destructive"
          onClick={() => {
            if (confirm(`Delete "${row.original.name}"?`)) {
              deleteMutation.mutate({ data: { id: row.original.id } })
            }
          }}
        >
          <Trash2 className="h-3 w-3" />
        </Button>
      ),
    },
  ]

  return (
    <>
      <Card>
        <CardHeader className="flex flex-row items-center justify-between">
          <CardTitle>Scanning Tools</CardTitle>
          <Button size="sm" onClick={() => setCreateOpen(true)}>
            <Plus className="mr-1 h-4 w-4" />
            New Tool
          </Button>
        </CardHeader>
        <CardContent>
          <DataTable columns={columns} data={query.data?.items ?? []} />
          <PaginationControls
            page={page}
            pageSize={pageSize}
            totalCount={query.data?.totalCount ?? 0}
            onPageChange={onPageChange}
          />
        </CardContent>
      </Card>

      <Dialog open={createOpen} onOpenChange={setCreateOpen}>
        <DialogContent className="max-w-lg">
          <DialogHeader>
            <DialogTitle>New Scanning Tool</DialogTitle>
            <DialogDescription>Define a script that runs on target hosts and produces structured output.</DialogDescription>
          </DialogHeader>
          <div className="space-y-4 max-h-[60vh] overflow-y-auto pr-1">
            <div>
              <Label>Name</Label>
              <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="linux-software-inventory" />
            </div>
            <div>
              <Label>Description</Label>
              <Textarea value={description} onChange={(e) => setDescription(e.target.value)} />
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <Label>Script Type</Label>
                <Select
                  value={scriptType}
                  onValueChange={(v) => {
                    const st = v as (typeof scriptTypes)[number]
                    setScriptType(st)
                    setInterpreterPath(defaultInterpreters[st] ?? '')
                  }}
                >
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    {scriptTypes.map((t) => (
                      <SelectItem key={t} value={t}>{t}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div>
                <Label>Timeout (seconds)</Label>
                <Input type="number" value={timeoutSeconds} onChange={(e) => setTimeoutSeconds(Number(e.target.value))} min={5} max={3600} />
              </div>
            </div>
            <div>
              <Label>Interpreter Path</Label>
              <Input value={interpreterPath} onChange={(e) => setInterpreterPath(e.target.value)} />
            </div>
            <div>
              <Label>Initial Script</Label>
              <Textarea
                value={initialScript}
                onChange={(e) => setInitialScript(e.target.value)}
                rows={8}
                className="font-mono text-xs"
                placeholder="# Your script here..."
              />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setCreateOpen(false)}>Cancel</Button>
            <Button
              onClick={() =>
                createMutation.mutate({
                  data: { name, description, scriptType, interpreterPath, timeoutSeconds, initialScript },
                })
              }
              disabled={!name || !interpreterPath || !initialScript || createMutation.isPending}
            >
              Create Tool
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}
```

- [ ] **Step 2: Wire into workbench route**

In `authenticated-scans.tsx`, add import:
```typescript
import { ScanningToolsTab } from '@/components/features/admin/scanning-tools/ScanningToolsTab'
```

Replace tools TabsContent:
```typescript
        <TabsContent value="tools" className="space-y-4 pt-1">
          {'tools' in loaderData && loaderData.tools && (
            <ScanningToolsTab
              initialData={loaderData.tools}
              page={search.page}
              pageSize={search.pageSize}
              onPageChange={(p) => navigate({ search: { ...search, page: p } })}
            />
          )}
        </TabsContent>
```

- [ ] **Step 3: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1 | head -20`

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/features/admin/scanning-tools/ \
  frontend/src/routes/_authed/admin/authenticated-scans.tsx
git commit -m "feat: add Scanning Tools tab with create dialog"
```

---

## Task 7: Scan Profiles tab

**Files:**
- Create: `frontend/src/components/features/admin/scan-profiles/ScanProfileDialog.tsx`
- Create: `frontend/src/components/features/admin/scan-profiles/ScanProfilesTab.tsx`
- Modify: `frontend/src/routes/_authed/admin/authenticated-scans.tsx`

- [ ] **Step 1: Create ScanProfileDialog**

```typescript
import { useState, useEffect } from 'react'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import type { ConnectionProfile, ScanProfile, ScanRunner, ScanningTool } from '@/api/authenticated-scans.schemas'

type Props = {
  open: boolean
  onOpenChange: (open: boolean) => void
  profile?: ScanProfile | null
  runners: ScanRunner[]
  connections: ConnectionProfile[]
  tools: ScanningTool[]
  onSubmit: (data: {
    name: string
    description: string
    cronSchedule: string
    connectionProfileId: string
    scanRunnerId: string
    enabled: boolean
    toolIds: string[]
  }) => void
  isPending: boolean
}

export function ScanProfileDialog({
  open,
  onOpenChange,
  profile,
  runners,
  connections,
  tools,
  onSubmit,
  isPending,
}: Props) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [cronSchedule, setCronSchedule] = useState('')
  const [connectionProfileId, setConnectionProfileId] = useState('')
  const [scanRunnerId, setScanRunnerId] = useState('')
  const [enabled, setEnabled] = useState(true)
  const [selectedToolIds, setSelectedToolIds] = useState<string[]>([])

  useEffect(() => {
    if (profile) {
      setName(profile.name)
      setDescription(profile.description)
      setCronSchedule(profile.cronSchedule)
      setConnectionProfileId(profile.connectionProfileId)
      setScanRunnerId(profile.scanRunnerId)
      setEnabled(profile.enabled)
      setSelectedToolIds(profile.toolIds)
    } else {
      setName('')
      setDescription('')
      setCronSchedule('')
      setConnectionProfileId(connections[0]?.id ?? '')
      setScanRunnerId(runners[0]?.id ?? '')
      setEnabled(true)
      setSelectedToolIds([])
    }
  }, [profile, open, connections, runners])

  const toggleTool = (toolId: string) => {
    setSelectedToolIds((prev) =>
      prev.includes(toolId) ? prev.filter((id) => id !== toolId) : [...prev, toolId],
    )
  }

  const canSubmit = name && connectionProfileId && scanRunnerId

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>{profile ? 'Edit' : 'New'} Scan Profile</DialogTitle>
          <DialogDescription>Bundle tools, schedule, and runner into a scan configuration.</DialogDescription>
        </DialogHeader>
        <div className="space-y-4 max-h-[60vh] overflow-y-auto pr-1">
          <div>
            <Label>Name</Label>
            <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="weekly-linux-scan" />
          </div>
          <div>
            <Label>Description</Label>
            <Textarea value={description} onChange={(e) => setDescription(e.target.value)} />
          </div>
          <div>
            <Label>Cron Schedule (empty = manual only)</Label>
            <Input value={cronSchedule} onChange={(e) => setCronSchedule(e.target.value)} placeholder="0 2 * * 0" />
            {cronSchedule && (
              <p className="text-muted-foreground text-xs mt-1">
                Cron: {cronSchedule || 'manual only'}
              </p>
            )}
          </div>
          <div>
            <Label>Runner</Label>
            <Select value={scanRunnerId} onValueChange={setScanRunnerId}>
              <SelectTrigger><SelectValue placeholder="Select runner" /></SelectTrigger>
              <SelectContent>
                {runners.map((r) => (
                  <SelectItem key={r.id} value={r.id}>{r.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div>
            <Label>Connection Profile</Label>
            <Select value={connectionProfileId} onValueChange={setConnectionProfileId}>
              <SelectTrigger><SelectValue placeholder="Select connection" /></SelectTrigger>
              <SelectContent>
                {connections.map((c) => (
                  <SelectItem key={c.id} value={c.id}>
                    {c.name} ({c.sshHost}:{c.sshPort})
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div>
            <Label>Tools</Label>
            <div className="mt-1 space-y-2 rounded border p-2">
              {tools.length === 0 && (
                <p className="text-muted-foreground text-sm">No tools created yet.</p>
              )}
              {tools.map((tool) => (
                <label key={tool.id} className="flex items-center gap-2 text-sm cursor-pointer">
                  <input
                    type="checkbox"
                    checked={selectedToolIds.includes(tool.id)}
                    onChange={() => toggleTool(tool.id)}
                  />
                  {tool.name}
                  <span className="text-muted-foreground">({tool.scriptType})</span>
                </label>
              ))}
            </div>
          </div>
          <div className="flex items-center gap-2">
            <Switch checked={enabled} onCheckedChange={setEnabled} />
            <Label>Enabled</Label>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
          <Button
            onClick={() =>
              onSubmit({
                name,
                description,
                cronSchedule,
                connectionProfileId,
                scanRunnerId,
                enabled,
                toolIds: selectedToolIds,
              })
            }
            disabled={!canSubmit || isPending}
          >
            {profile ? 'Save' : 'Create'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
```

- [ ] **Step 2: Create ScanProfilesTab**

```typescript
import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { toast } from 'sonner'
import { PenSquare, Play, Plus, Trash2 } from 'lucide-react'
import {
  createScanProfile,
  deleteScanProfile,
  fetchScanProfiles,
  triggerScanRun,
  updateScanProfile,
} from '@/api/authenticated-scans.functions'
import type {
  ConnectionProfile,
  PagedScanProfiles,
  ScanProfile,
  ScanRunner,
  ScanningTool,
} from '@/api/authenticated-scans.schemas'
import { ScanProfileDialog } from './ScanProfileDialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import { PaginationControls } from '@/components/ui/pagination-controls'

type Props = {
  initialData: PagedScanProfiles
  runners: ScanRunner[]
  connections: ConnectionProfile[]
  tools: ScanningTool[]
  page: number
  pageSize: number
  onPageChange: (page: number) => void
}

export function ScanProfilesTab({ initialData, runners, connections, tools, page, pageSize, onPageChange }: Props) {
  const queryClient = useQueryClient()
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editing, setEditing] = useState<ScanProfile | null>(null)

  const query = useQuery({
    queryKey: ['scan-profiles', page, pageSize],
    queryFn: () => fetchScanProfiles({ data: { page, pageSize } }),
    initialData,
  })

  const createMutation = useMutation({
    mutationFn: createScanProfile,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scan-profiles'] })
      setDialogOpen(false)
      toast.success('Scan profile created')
    },
    onError: () => toast.error('Failed to create scan profile'),
  })

  const updateMutation = useMutation({
    mutationFn: updateScanProfile,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scan-profiles'] })
      setDialogOpen(false)
      setEditing(null)
      toast.success('Scan profile updated')
    },
    onError: () => toast.error('Failed to update scan profile'),
  })

  const deleteMutation = useMutation({
    mutationFn: deleteScanProfile,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scan-profiles'] })
      toast.success('Scan profile deleted')
    },
    onError: () => toast.error('Failed to delete scan profile'),
  })

  const triggerMutation = useMutation({
    mutationFn: triggerScanRun,
    onSuccess: () => toast.success('Scan run triggered'),
    onError: () => toast.error('Failed to trigger scan run'),
  })

  const runnerName = (id: string) => runners.find((r) => r.id === id)?.name ?? '—'
  const connectionName = (id: string) => connections.find((c) => c.id === id)?.name ?? '—'

  const columns: ColumnDef<ScanProfile>[] = [
    { accessorKey: 'name', header: 'Name' },
    {
      id: 'schedule',
      header: 'Schedule',
      cell: ({ row }) => row.original.cronSchedule || <span className="text-muted-foreground">Manual</span>,
    },
    {
      id: 'runner',
      header: 'Runner',
      cell: ({ row }) => runnerName(row.original.scanRunnerId),
    },
    {
      id: 'connection',
      header: 'Connection',
      cell: ({ row }) => connectionName(row.original.connectionProfileId),
    },
    {
      id: 'tools',
      header: 'Tools',
      cell: ({ row }) => row.original.toolIds.length,
    },
    {
      id: 'enabled',
      header: 'Enabled',
      cell: ({ row }) =>
        row.original.enabled ? (
          <Badge variant="default" className="bg-green-600">Yes</Badge>
        ) : (
          <Badge variant="secondary">No</Badge>
        ),
    },
    {
      id: 'actions',
      cell: ({ row }) => (
        <div className="flex gap-1">
          <Button
            size="sm"
            variant="ghost"
            onClick={() => triggerMutation.mutate({ data: { id: row.original.id } })}
            disabled={triggerMutation.isPending}
            title="Trigger manual run"
          >
            <Play className="h-3 w-3" />
          </Button>
          <Button
            size="sm"
            variant="ghost"
            onClick={() => {
              setEditing(row.original)
              setDialogOpen(true)
            }}
          >
            <PenSquare className="h-3 w-3" />
          </Button>
          <Button
            size="sm"
            variant="ghost"
            className="text-destructive"
            onClick={() => {
              if (confirm(`Delete "${row.original.name}"?`)) {
                deleteMutation.mutate({ data: { id: row.original.id } })
              }
            }}
          >
            <Trash2 className="h-3 w-3" />
          </Button>
        </div>
      ),
    },
  ]

  return (
    <>
      <Card>
        <CardHeader className="flex flex-row items-center justify-between">
          <CardTitle>Scan Profiles</CardTitle>
          <Button
            size="sm"
            onClick={() => {
              setEditing(null)
              setDialogOpen(true)
            }}
          >
            <Plus className="mr-1 h-4 w-4" />
            New Profile
          </Button>
        </CardHeader>
        <CardContent>
          <DataTable columns={columns} data={query.data?.items ?? []} />
          <PaginationControls
            page={page}
            pageSize={pageSize}
            totalCount={query.data?.totalCount ?? 0}
            onPageChange={onPageChange}
          />
        </CardContent>
      </Card>

      <ScanProfileDialog
        open={dialogOpen}
        onOpenChange={(open) => {
          setDialogOpen(open)
          if (!open) setEditing(null)
        }}
        profile={editing}
        runners={runners}
        connections={connections}
        tools={tools}
        isPending={createMutation.isPending || updateMutation.isPending}
        onSubmit={(data) => {
          if (editing) {
            updateMutation.mutate({ data: { id: editing.id, ...data } })
          } else {
            createMutation.mutate({ data })
          }
        }}
      />
    </>
  )
}
```

- [ ] **Step 3: Wire into workbench route**

In `authenticated-scans.tsx`, add import:
```typescript
import { ScanProfilesTab } from '@/components/features/admin/scan-profiles/ScanProfilesTab'
```

Replace profiles TabsContent:
```typescript
        <TabsContent value="profiles" className="space-y-4 pt-1">
          {'profiles' in loaderData && loaderData.profiles && (
            <ScanProfilesTab
              initialData={loaderData.profiles}
              runners={loaderData.runners?.items ?? []}
              connections={loaderData.connections?.items ?? []}
              tools={loaderData.tools?.items ?? []}
              page={search.page}
              pageSize={search.pageSize}
              onPageChange={(p) => navigate({ search: { ...search, page: p } })}
            />
          )}
        </TabsContent>
```

- [ ] **Step 4: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1 | head -20`

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/features/admin/scan-profiles/ \
  frontend/src/routes/_authed/admin/authenticated-scans.tsx
git commit -m "feat: add Scan Profiles tab with create/edit dialog and manual trigger"
```

---

## Task 8: Add workbench link to admin hub

**Files:**
- Modify: `frontend/src/routes/_authed/admin/index.tsx`

- [ ] **Step 1: Add authenticated scans card to admin hub**

Find the admin areas/sections definition in `frontend/src/routes/_authed/admin/index.tsx`. Add an entry for Authenticated Scans in the appropriate section (likely "Configuration" or "Platform"). The entry should:

- Title: "Authenticated Scans"
- Description: "Configure on-prem scan runners, tools, connections, and profiles"
- Route: `/admin/authenticated-scans`
- Required roles: `['CustomerAdmin', 'GlobalAdmin']`

Follow the existing pattern of `AdminArea` / `AdminSection` definitions already in the file. Read the file first to find the exact pattern, then add the entry.

- [ ] **Step 2: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1 | head -20`

- [ ] **Step 3: Commit**

```bash
git add frontend/src/routes/_authed/admin/index.tsx
git commit -m "feat: add Authenticated Scans link to admin hub"
```

---

## Task 9: Full build verification

- [ ] **Step 1: TypeScript check**

Run: `cd frontend && npx tsc --noEmit`
Expected: No errors.

- [ ] **Step 2: Build**

Run: `cd frontend && npm run build`
Expected: Build succeeds.

- [ ] **Step 3: Full backend test suite (unchanged)**

Run: `dotnet test --nologo`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git commit --allow-empty -m "chore: Plan 4 authenticated scans admin UI workbench complete"
```

---

## Self-Review

**Spec coverage (§8):**

| Requirement | Task | Status |
|---|---|---|
| §8.1 Workbench route `/admin/authenticated-scans` | Task 3 | Covered |
| §8.1 4 tabs deep-linked via `?tab=` | Task 3 | Covered |
| §8.1 Tab 1 — Scan Profiles data table | Task 7 | Covered |
| §8.1 Tab 1 — Create/edit dialog with runner/connection/tool selects | Task 7 | Covered |
| §8.1 Tab 1 — Cron input | Task 7 | Basic input (cron preview deferred to Plan 5) |
| §8.1 Tab 1 — Detail drawer with assigned devices | — | Deferred to Plan 5 |
| §8.1 Tab 2 — Scanning Tools data table | Task 6 | Covered |
| §8.1 Tab 2 — Create dialog (basic) | Task 6 | Covered (CodeMirror editor in Plan 5) |
| §8.1 Tab 2 — Version history panel | — | Deferred to Plan 5 |
| §8.1 Tab 3 — Connection Profiles data table + CRUD | Task 5 | Covered |
| §8.1 Tab 3 — Secret masking with "Replace" | Task 5 | Covered (password never echoed back) |
| §8.1 Tab 4 — Scan Runners data table | Task 4 | Covered |
| §8.1 Tab 4 — Create with secret-once modal | Task 4 | Covered |
| §8.1 Tab 4 — Rotate secret | Task 4 | Covered |
| §8.1 Role gating on Admin | Task 3 | Covered (beforeLoad check) |
| Manual trigger on profiles | Task 7 | Covered |
| Admin hub link | Task 8 | Covered |

**Deferred to Plan 5:** CodeMirror script editor, version history, cron preview, assigned devices drawer, sources admin tab, asset rule `AssignScanProfile` operation, run report dialog.

**Placeholder scan:** No TBD/TODO found.

**Type consistency:** All schema types (`ScanProfile`, `ScanRunner`, `ConnectionProfile`, `ScanningTool`) used consistently across schemas → functions → components. Props types match loader return shapes.
