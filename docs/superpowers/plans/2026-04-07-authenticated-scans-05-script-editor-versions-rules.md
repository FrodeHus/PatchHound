# Authenticated Scans Plan 5: Script Editor, Version History & Asset Rule Integration

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enhance the Scanning Tools tab with a CodeMirror script editor, version history panel, and publish-new-version workflow; add the "Assign Scan Profile" operation to the asset rule wizard.

**Architecture:** Install `@codemirror/lang-python` for Python highlighting and use `@codemirror/legacy-modes` for bash/powershell via `StreamLanguage`. Add server functions to call existing backend endpoints (`POST /scanning-tools/{id}/versions`, `GET /scanning-tools/{id}/versions`). Extend `AssetRuleWizard` with a new `OperationEditor` entry using the existing pattern. All changes are frontend-only — the backend API endpoints already exist.

**Tech Stack:** React 19, CodeMirror 6 (`@uiw/react-codemirror`, `@codemirror/lang-python`, `@codemirror/legacy-modes`), TanStack Router + React Query, shadcn/ui, Zod

---

## Scope

**In scope:**
- Install CodeMirror language packages for Python, Bash, PowerShell
- Create a reusable `ScriptEditor` component wrapping CodeMirror with language switching
- Enhance `ScanningToolsTab` with an edit/detail view showing CodeMirror editor + version history
- Server functions for publish version and list versions
- "Assign Scan Profile" operation in the asset rule wizard
- Build verification

**Out of scope (Plan 6):** Sources admin "Authenticated Scans" tab, run report dialog (requires new backend controller for scan runs).

---

## File Map

| File | Responsibility |
|------|---------------|
| `frontend/src/components/ui/script-editor.tsx` | Reusable CodeMirror wrapper with Python/Bash/PowerShell language support |
| `frontend/src/api/authenticated-scans.schemas.ts` | Add `scanningToolVersionSchema` list schema (already has individual schema) |
| `frontend/src/api/authenticated-scans.functions.ts` | Add `publishToolVersion` and `fetchToolVersions` server functions |
| `frontend/src/components/features/admin/scanning-tools/ScanningToolEditor.tsx` | Edit/detail view: metadata form + script editor + publish button |
| `frontend/src/components/features/admin/scanning-tools/VersionHistoryPanel.tsx` | Version list panel with view-version button |
| `frontend/src/components/features/admin/scanning-tools/ScanningToolsTab.tsx` | Modify: add row click → editor, back button |
| `frontend/src/routes/_authed/admin/authenticated-scans.tsx` | Modify: pass tool detail data, handle `toolId` search param |
| `frontend/src/components/features/admin/asset-rules/AssetRuleWizard.tsx` | Modify: add AssignScanProfile OperationEditor |
| `frontend/src/routes/_authed/admin/asset-rules/new.tsx` | Modify: load scan profiles in loader |
| `frontend/src/routes/_authed/admin/asset-rules/$id.tsx` | Modify: load scan profiles in loader |

---

## Task 1: Install CodeMirror language packages

**Files:**
- Modify: `frontend/package.json`

- [ ] **Step 1: Install packages**

```bash
cd frontend && npm install @codemirror/lang-python @codemirror/legacy-modes
```

- [ ] **Step 2: Verify installation**

Run: `cd frontend && node -e "require('@codemirror/lang-python'); require('@codemirror/legacy-modes/mode/shell'); require('@codemirror/legacy-modes/mode/powershell'); console.log('OK')"`
Expected: `OK`

- [ ] **Step 3: Commit**

```bash
git add frontend/package.json frontend/package-lock.json
git commit -m "chore: install CodeMirror language packages for Python, Bash, PowerShell"
```

---

## Task 2: Create ScriptEditor component

**Files:**
- Create: `frontend/src/components/ui/script-editor.tsx`

- [ ] **Step 1: Create the component**

```typescript
import { useMemo } from 'react'
import CodeMirror from '@uiw/react-codemirror'
import { python } from '@codemirror/lang-python'
import { StreamLanguage } from '@codemirror/language'
import { shell } from '@codemirror/legacy-modes/mode/shell'
import { powerShell } from '@codemirror/legacy-modes/mode/powershell'
import { EditorView } from '@codemirror/view'
import { cn } from '@/lib/utils'

type Props = {
  value: string
  onChange?: (value: string) => void
  language: 'python' | 'bash' | 'powershell'
  readOnly?: boolean
  height?: string
  className?: string
}

const baseTheme = EditorView.theme({
  '&': { fontSize: '13px' },
  '.cm-gutters': {
    backgroundColor: 'hsl(var(--muted))',
    borderRight: '1px solid hsl(var(--border))',
  },
  '.cm-activeLineGutter': { backgroundColor: 'hsl(var(--accent))' },
  '.cm-activeLine': { backgroundColor: 'hsl(var(--accent) / 0.3)' },
})

function getLanguageExtension(lang: Props['language']) {
  switch (lang) {
    case 'python':
      return python()
    case 'bash':
      return StreamLanguage.define(shell)
    case 'powershell':
      return StreamLanguage.define(powerShell)
  }
}

export function ScriptEditor({ value, onChange, language, readOnly, height = '400px', className }: Props) {
  const extensions = useMemo(
    () => [baseTheme, getLanguageExtension(language)],
    [language],
  )

  return (
    <div className={cn('overflow-hidden rounded-lg border', className)}>
      <CodeMirror
        value={value}
        onChange={onChange}
        extensions={extensions}
        readOnly={readOnly}
        height={height}
        basicSetup={{
          lineNumbers: true,
          foldGutter: true,
          highlightActiveLine: true,
          bracketMatching: true,
          indentOnInput: true,
        }}
      />
    </div>
  )
}
```

- [ ] **Step 2: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1 | head -20`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/ui/script-editor.tsx
git commit -m "feat: add ScriptEditor component with Python/Bash/PowerShell highlighting"
```

---

## Task 3: Add version server functions and schemas

**Files:**
- Modify: `frontend/src/api/authenticated-scans.schemas.ts`
- Modify: `frontend/src/api/authenticated-scans.functions.ts`

- [ ] **Step 1: Add version list schema to schemas file**

In `frontend/src/api/authenticated-scans.schemas.ts`, add after the existing `scanningToolVersionSchema` type export:

```typescript
export const scanningToolVersionListSchema = z.array(scanningToolVersionSchema)
```

- [ ] **Step 2: Add server functions for versions**

In `frontend/src/api/authenticated-scans.functions.ts`, add to the "Scanning Tools" section:

```typescript
import {
  // ... existing imports ...
  scanningToolVersionListSchema,
  scanningToolSchema,
} from './authenticated-scans.schemas'

export const fetchToolVersions = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ toolId: z.string().uuid() }))
  .handler(async ({ context, data: { toolId } }) => {
    return scanningToolVersionListSchema.parse(
      await apiGet(`/scanning-tools/${toolId}/versions`, context),
    )
  })

export const publishToolVersion = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ toolId: z.string().uuid(), scriptContent: z.string().min(1) }))
  .handler(async ({ context, data: { toolId, scriptContent } }) => {
    return await apiPost(`/scanning-tools/${toolId}/versions`, context, { scriptContent })
  })

export const fetchScanningTool = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ id: z.string().uuid() }))
  .handler(async ({ context, data: { id } }) => {
    return scanningToolSchema.parse(await apiGet(`/scanning-tools/${id}`, context))
  })
```

Note: `scanningToolSchema` is already imported in the schemas file and the functions file already imports from that file. You just need to add it to the existing import statement.

- [ ] **Step 3: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1 | head -20`
Expected: No errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/authenticated-scans.schemas.ts \
  frontend/src/api/authenticated-scans.functions.ts
git commit -m "feat: add server functions for tool versions (list, publish, fetch single)"
```

---

## Task 4: Create VersionHistoryPanel component

**Files:**
- Create: `frontend/src/components/features/admin/scanning-tools/VersionHistoryPanel.tsx`

- [ ] **Step 1: Create the component**

```typescript
import { useQuery } from '@tanstack/react-query'
import { Eye } from 'lucide-react'
import { fetchToolVersions } from '@/api/authenticated-scans.functions'
import type { ScanningToolVersion } from '@/api/authenticated-scans.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { formatDateTime } from '@/lib/formatting'

type Props = {
  toolId: string
  currentVersionId: string | null
  onViewVersion: (version: ScanningToolVersion) => void
}

export function VersionHistoryPanel({ toolId, currentVersionId, onViewVersion }: Props) {
  const query = useQuery({
    queryKey: ['tool-versions', toolId],
    queryFn: () => fetchToolVersions({ data: { toolId } }),
  })

  const versions = query.data ?? []

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Version History</CardTitle>
      </CardHeader>
      <CardContent>
        {versions.length === 0 ? (
          <p className="text-muted-foreground text-sm">No versions yet.</p>
        ) : (
          <div className="space-y-2">
            {versions.map((v) => (
              <div
                key={v.id}
                className="flex items-center justify-between rounded-lg border px-3 py-2 text-sm"
              >
                <div className="space-y-0.5">
                  <div className="flex items-center gap-2">
                    <span className="font-medium">v{v.versionNumber}</span>
                    {v.id === currentVersionId && (
                      <Badge variant="default" className="text-xs">Current</Badge>
                    )}
                  </div>
                  <p className="text-muted-foreground text-xs">
                    {formatDateTime(v.editedAt)}
                  </p>
                </div>
                <Button
                  size="sm"
                  variant="ghost"
                  onClick={() => onViewVersion(v)}
                >
                  <Eye className="mr-1 h-3 w-3" />
                  View
                </Button>
              </div>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  )
}
```

- [ ] **Step 2: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1 | head -20`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/features/admin/scanning-tools/VersionHistoryPanel.tsx
git commit -m "feat: add VersionHistoryPanel component for scanning tool versions"
```

---

## Task 5: Create ScanningToolEditor component

**Files:**
- Create: `frontend/src/components/features/admin/scanning-tools/ScanningToolEditor.tsx`

- [ ] **Step 1: Create the editor component**

```typescript
import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { ArrowLeft, Save } from 'lucide-react'
import { publishToolVersion, updateScanningTool } from '@/api/authenticated-scans.functions'
import type { ScanningTool, ScanningToolVersion } from '@/api/authenticated-scans.schemas'
import { ScriptEditor } from '@/components/ui/script-editor'
import { VersionHistoryPanel } from './VersionHistoryPanel'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'

const scriptTypes = ['python', 'bash', 'powershell'] as const
const defaultInterpreters: Record<string, string> = {
  python: '/usr/bin/python3',
  bash: '/bin/bash',
  powershell: '/usr/bin/pwsh',
}

type Props = {
  tool: ScanningTool
  initialScript: string
  onBack: () => void
}

export function ScanningToolEditor({ tool, initialScript, onBack }: Props) {
  const queryClient = useQueryClient()

  // Metadata state
  const [name, setName] = useState(tool.name)
  const [description, setDescription] = useState(tool.description)
  const [scriptType, setScriptType] = useState<(typeof scriptTypes)[number]>(
    tool.scriptType as (typeof scriptTypes)[number],
  )
  const [interpreterPath, setInterpreterPath] = useState(tool.interpreterPath)
  const [timeoutSeconds, setTimeoutSeconds] = useState(tool.timeoutSeconds)

  // Script state
  const [scriptContent, setScriptContent] = useState(initialScript)
  const [viewingVersion, setViewingVersion] = useState<ScanningToolVersion | null>(null)

  const metadataMutation = useMutation({
    mutationFn: updateScanningTool,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scanning-tools'] })
      toast.success('Tool metadata updated')
    },
    onError: () => toast.error('Failed to update tool metadata'),
  })

  const publishMutation = useMutation({
    mutationFn: publishToolVersion,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tool-versions', tool.id] })
      queryClient.invalidateQueries({ queryKey: ['scanning-tools'] })
      toast.success('New version published')
    },
    onError: () => toast.error('Failed to publish version'),
  })

  const handleViewVersion = (version: ScanningToolVersion) => {
    setViewingVersion(version)
    setScriptContent(version.scriptContent)
  }

  const handleRestoreEditor = () => {
    setViewingVersion(null)
    // Script stays as-is — user can publish it or discard
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3">
        <Button variant="ghost" size="sm" onClick={onBack}>
          <ArrowLeft className="mr-1 h-4 w-4" />
          Back
        </Button>
        <h2 className="text-lg font-semibold">{tool.name}</h2>
        <Badge variant="outline">{tool.scriptType}</Badge>
      </div>

      <div className="grid gap-4 lg:grid-cols-[1fr_300px]">
        {/* Left: metadata + editor */}
        <div className="space-y-4">
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Tool Settings</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <Label>Name</Label>
                  <Input value={name} onChange={(e) => setName(e.target.value)} />
                </div>
                <div>
                  <Label>Timeout (seconds)</Label>
                  <Input
                    type="number"
                    value={timeoutSeconds}
                    onChange={(e) => setTimeoutSeconds(Number(e.target.value))}
                    min={5}
                    max={3600}
                  />
                </div>
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
                      if (!v) return
                      const st = v as (typeof scriptTypes)[number]
                      setScriptType(st)
                      setInterpreterPath(defaultInterpreters[st] ?? interpreterPath)
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
                  <Label>Interpreter Path</Label>
                  <Input value={interpreterPath} onChange={(e) => setInterpreterPath(e.target.value)} />
                </div>
              </div>
              <Button
                size="sm"
                onClick={() =>
                  metadataMutation.mutate({
                    data: { id: tool.id, name, description, scriptType, interpreterPath, timeoutSeconds },
                  })
                }
                disabled={!name || !interpreterPath || metadataMutation.isPending}
              >
                Save Settings
              </Button>
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="flex flex-row items-center justify-between">
              <div>
                <CardTitle className="text-base">Script</CardTitle>
                {viewingVersion && (
                  <p className="text-muted-foreground text-xs mt-1">
                    Viewing v{viewingVersion.versionNumber} —{' '}
                    <button
                      type="button"
                      className="text-primary underline"
                      onClick={handleRestoreEditor}
                    >
                      back to editor
                    </button>
                  </p>
                )}
              </div>
              <Button
                size="sm"
                onClick={() =>
                  publishMutation.mutate({ data: { toolId: tool.id, scriptContent } })
                }
                disabled={!scriptContent || publishMutation.isPending}
              >
                <Save className="mr-1 h-3 w-3" />
                Publish Version
              </Button>
            </CardHeader>
            <CardContent>
              <ScriptEditor
                value={scriptContent}
                onChange={viewingVersion ? undefined : setScriptContent}
                language={scriptType}
                readOnly={!!viewingVersion}
                height="400px"
              />
            </CardContent>
          </Card>
        </div>

        {/* Right: version history */}
        <div>
          <VersionHistoryPanel
            toolId={tool.id}
            currentVersionId={tool.currentVersionId}
            onViewVersion={handleViewVersion}
          />
        </div>
      </div>
    </div>
  )
}
```

- [ ] **Step 2: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1 | head -20`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/features/admin/scanning-tools/ScanningToolEditor.tsx
git commit -m "feat: add ScanningToolEditor with CodeMirror script editing and version management"
```

---

## Task 6: Wire editor into ScanningToolsTab and workbench route

**Files:**
- Modify: `frontend/src/routes/_authed/admin/authenticated-scans.tsx`
- Modify: `frontend/src/components/features/admin/scanning-tools/ScanningToolsTab.tsx`

- [ ] **Step 1: Update the route search params and loader**

In `frontend/src/routes/_authed/admin/authenticated-scans.tsx`:

1. Add to the `validateSearch` schema:
```typescript
  validateSearch: baseListSearchSchema.extend({
    tab: z.enum(tabValues).optional(),
    toolId: z.string().uuid().optional(),
  }),
```

2. Add imports:
```typescript
import { fetchScanningTool, fetchToolVersions } from '@/api/authenticated-scans.functions'
```

3. Update the `tools` tab loader branch to also fetch tool detail + versions when `toolId` is present:
```typescript
    if (tab === 'tools') {
      const tools = await fetchScanningTools({ data: paging })
      if (deps.toolId) {
        const [tool, versions] = await Promise.all([
          fetchScanningTool({ data: { id: deps.toolId } }),
          fetchToolVersions({ data: { toolId: deps.toolId } }),
        ])
        const currentScript = versions.find((v) => v.id === tool.currentVersionId)?.scriptContent ?? ''
        return { tools, toolDetail: tool, toolVersions: versions, currentScript, tab }
      }
      return { tools, tab }
    }
```

4. Update the tools TabsContent to pass the detail data:
```typescript
        <TabsContent value="tools" className="space-y-4 pt-1">
          {'tools' in loaderData && loaderData.tools && (
            <ScanningToolsTab
              initialData={loaderData.tools}
              toolDetail={'toolDetail' in loaderData ? loaderData.toolDetail : undefined}
              currentScript={'currentScript' in loaderData ? loaderData.currentScript : undefined}
              page={search.page}
              pageSize={search.pageSize}
              onPageChange={(p) => navigate({ search: { ...search, page: p } })}
              onPageSizeChange={(ps) => navigate({ search: { ...search, page: 1, pageSize: ps } })}
              onSelectTool={(id) => navigate({ search: { ...search, toolId: id } })}
              onDeselectTool={() => navigate({ search: { ...search, toolId: undefined } })}
            />
          )}
        </TabsContent>
```

- [ ] **Step 2: Update ScanningToolsTab to show editor when tool is selected**

In `frontend/src/components/features/admin/scanning-tools/ScanningToolsTab.tsx`:

1. Add imports:
```typescript
import { ScanningToolEditor } from './ScanningToolEditor'
import type { ScanningTool } from '@/api/authenticated-scans.schemas'
```

2. Update Props type:
```typescript
type Props = {
  initialData: PagedScanningTools
  toolDetail?: ScanningTool
  currentScript?: string
  page: number
  pageSize: number
  onPageChange: (page: number) => void
  onPageSizeChange?: (pageSize: number) => void
  onSelectTool: (id: string) => void
  onDeselectTool: () => void
}
```

3. Update the function signature:
```typescript
export function ScanningToolsTab({ initialData, toolDetail, currentScript, page, pageSize, onPageChange, onPageSizeChange, onSelectTool, onDeselectTool }: Props) {
```

4. If `toolDetail` is set, render the editor instead of the table:
```typescript
  // At the top of the component body, before the return:
  if (toolDetail) {
    return (
      <ScanningToolEditor
        tool={toolDetail}
        initialScript={currentScript ?? ''}
        onBack={onDeselectTool}
      />
    )
  }
```

5. Add a click handler on each row's name to navigate to the editor. Replace the name column definition:
```typescript
    {
      accessorKey: 'name',
      header: 'Name',
      cell: ({ row }) => (
        <button
          type="button"
          className="text-primary underline text-left"
          onClick={() => onSelectTool(row.original.id)}
        >
          {row.original.name}
        </button>
      ),
    },
```

- [ ] **Step 3: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1 | head -20`
Expected: No errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/routes/_authed/admin/authenticated-scans.tsx \
  frontend/src/components/features/admin/scanning-tools/ScanningToolsTab.tsx
git commit -m "feat: wire ScanningToolEditor into tools tab with URL-driven detail view"
```

---

## Task 7: Add AssignScanProfile operation to asset rule wizard

**Files:**
- Modify: `frontend/src/components/features/admin/asset-rules/AssetRuleWizard.tsx`
- Modify: `frontend/src/routes/_authed/admin/asset-rules/new.tsx`
- Modify: `frontend/src/routes/_authed/admin/asset-rules/$id.tsx`

- [ ] **Step 1: Add scan profiles to the wizard props**

In `frontend/src/components/features/admin/asset-rules/AssetRuleWizard.tsx`:

1. Add import:
```typescript
import type { ScanProfile } from '@/api/authenticated-scans.schemas'
```

2. Add to `AssetRuleWizardProps`:
```typescript
type AssetRuleWizardProps = {
  mode: 'create' | 'edit'
  initialData?: AssetRule
  securityProfiles: SecurityProfile[]
  businessLabels: BusinessLabel[]
  teams: TeamItem[]
  scanProfiles: ScanProfile[]
}
```

3. Update function signature to destructure `scanProfiles`:
```typescript
export function AssetRuleWizard({ mode, initialData, securityProfiles, businessLabels, teams, scanProfiles }: AssetRuleWizardProps) {
```

4. Add the `OperationEditor` for AssignScanProfile right after the "AssignBusinessLabel" OperationEditor (around line 298):
```typescript
            <OperationEditor
              type="AssignScanProfile"
              label="Assign Scan Profile"
              description="Assign an authenticated scan profile to matching assets for on-prem host scanning."
              options={scanProfiles.map((p) => ({
                value: p.id,
                label: p.name,
              }))}
              paramKey="scanProfileId"
              operations={operations}
              onChange={setOperations}
            />
```

5. Update the `describeOperationTarget` function to handle `AssignScanProfile`:
Add before the final `return operation.type`:
```typescript
  if (operation.type === 'AssignScanProfile') {
    return scanProfiles.find((p) => p.id === operation.parameters.scanProfileId)?.name
      ?? operation.parameters.scanProfileId
  }
```

Note: The `describeOperationTarget` function already receives parameters but doesn't have `scanProfiles`. You'll need to add it to the function's parameter list and all call sites. There are several helper functions (`describeOperationTarget`, `buildOperationImpactLines`, `buildPreviewHeadline`) that all pass the same set of lookup data. Add `scanProfiles` to each of them following the existing pattern.

6. Update `buildOperationImpactLines` to handle the new type:
Add a case to the switch:
```typescript
      case 'AssignScanProfile':
        return `assign scan profile ${describeOperationTarget(operation, securityProfiles, businessLabels, teams, scanProfiles)}`
```

- [ ] **Step 2: Update asset rule new page loader**

In `frontend/src/routes/_authed/admin/asset-rules/new.tsx`:

1. Add import:
```typescript
import { fetchScanProfiles } from '@/api/authenticated-scans.functions'
```

2. Update loader to also fetch scan profiles:
```typescript
  loader: async () => {
    const [profiles, businessLabels, teams, scanProfiles] = await Promise.all([
      fetchSecurityProfiles({ data: { pageSize: 100 } }),
      fetchBusinessLabels({ data: {} }),
      fetchTeams({ data: { pageSize: 100 } }),
      fetchScanProfiles({ data: { pageSize: 100 } }),
    ])
    return { profiles, businessLabels, teams, scanProfiles }
  },
```

3. Update component to pass scan profiles:
```typescript
function NewAssetRulePage() {
  const { profiles, businessLabels, teams, scanProfiles } = Route.useLoaderData()

  return (
    <section className="space-y-5">
      <div>
        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Asset Rules</p>
        <h1 className="text-2xl font-semibold tracking-[-0.04em]">Create Rule</h1>
      </div>
      <AssetRuleWizard
        mode="create"
        securityProfiles={profiles.items}
        businessLabels={businessLabels}
        teams={teams.items}
        scanProfiles={scanProfiles.items}
      />
    </section>
  )
}
```

- [ ] **Step 3: Update asset rule edit page loader**

Read `frontend/src/routes/_authed/admin/asset-rules/$id.tsx` first, then apply the same pattern: add `fetchScanProfiles` to the loader's `Promise.all`, destructure `scanProfiles` from loader data, pass `scanProfiles={scanProfiles.items}` to the `AssetRuleWizard`.

- [ ] **Step 4: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit 2>&1 | head -20`
Expected: No errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/features/admin/asset-rules/AssetRuleWizard.tsx \
  frontend/src/routes/_authed/admin/asset-rules/new.tsx \
  frontend/src/routes/_authed/admin/asset-rules/\$id.tsx
git commit -m "feat: add AssignScanProfile operation to asset rule wizard"
```

---

## Task 8: Full build verification

- [ ] **Step 1: TypeScript check**

Run: `cd frontend && npx tsc --noEmit`
Expected: No errors.

- [ ] **Step 2: Build**

Run: `cd frontend && npm run build`
Expected: Build succeeds.

- [ ] **Step 3: Backend test suite (unchanged)**

Run: `dotnet test --nologo`
Expected: All tests pass (no backend changes in this plan).

- [ ] **Step 4: Commit**

```bash
git commit --allow-empty -m "chore: Plan 5 script editor, version history, and asset rule integration complete"
```

---

## Self-Review

**Spec coverage:**

| Requirement | Task | Status |
|---|---|---|
| §8.2 — CodeMirror editor with syntax highlighting per language | Tasks 2, 5 | Covered |
| §8.2 — Version history panel: last 10 versions | Task 4 | Covered (backend already limits to 10) |
| §8.2 — Multi-step editor with name/description/script type/interpreter/timeout | Task 5 | Covered |
| §8.2 — Asset rule "AssignScanProfile" operation | Task 7 | Covered |
| §8.3 — Sources admin "Authenticated Scans" tab | — | Deferred to Plan 6 (needs new backend controller) |
| §8.3 — Run report dialog | — | Deferred to Plan 6 |
| §8.2 — "Show expected output JSON" button | — | Deferred to Plan 6 (nice-to-have, not blocking) |

**Placeholder scan:** No TBD/TODO/placeholders found.

**Type consistency:** `ScanningTool`, `ScanningToolVersion`, `ScanProfile` types used consistently across tasks. `scriptType` is typed as `'python' | 'bash' | 'powershell'` in both `ScriptEditor` and `ScanningToolEditor`.
