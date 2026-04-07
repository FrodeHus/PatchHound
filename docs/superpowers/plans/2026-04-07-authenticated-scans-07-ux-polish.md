# Authenticated Scans — Plan 7: UX Polish

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add remaining UX features from the design spec: expected output JSON panel, cron preview, drag-to-reorder tools, and assigned devices drawer.

**Architecture:** Four independent frontend tasks — no backend changes needed.

**Tech Stack:** TanStack Start, React, shadcn/ui, CodeMirror (`@codemirror/lang-json`), `cronstrue` for human-readable cron

---

## File Structure

| Action | File | Responsibility |
|--------|------|---------------|
| Create | `frontend/src/components/features/admin/scanning-tools/ExpectedOutputPanel.tsx` | Read-only JSON panel showing DetectedSoftware schema with required/optional legend |
| Modify | `frontend/src/components/features/admin/scanning-tools/ScanningToolEditor.tsx` | Add "Show expected output" toggle button that reveals the panel |
| Modify | `frontend/src/components/features/admin/scan-profiles/ScanProfileDialog.tsx` | Add cron preview text + drag-to-reorder tool list |
| Modify | `frontend/src/components/features/admin/scan-profiles/ScanProfilesTab.tsx` | Add assigned devices drawer (fetches DeviceScanProfileAssignments) |
| Modify | `frontend/src/api/authenticated-scans.schemas.ts` | Add `assetScanProfileAssignmentSchema` |
| Modify | `frontend/src/api/authenticated-scans.functions.ts` | Add `fetchProfileAssignedDevices` server function |

---

### Task 1: Expected output JSON panel

**Files:**
- Create: `frontend/src/components/features/admin/scanning-tools/ExpectedOutputPanel.tsx`
- Modify: `frontend/src/components/features/admin/scanning-tools/ScanningToolEditor.tsx`

- [ ] **Step 1: Install `@codemirror/lang-json`**

Run: `cd frontend && npm install @codemirror/lang-json`

- [ ] **Step 2: Create ExpectedOutputPanel component**

Create `frontend/src/components/features/admin/scanning-tools/ExpectedOutputPanel.tsx`:

```tsx
import { useMemo } from 'react'
import CodeMirror from '@uiw/react-codemirror'
import { json } from '@codemirror/lang-json'
import { EditorView } from '@codemirror/view'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

const EXPECTED_OUTPUT = `{
  "software": [
    {
      "canonicalName": "nginx",
      "canonicalProductKey": "nginx:nginx",
      "detectedVersion": "1.24.0",
      "canonicalVendor": "F5 Networks",
      "category": "web-server",
      "primaryCpe23Uri": "cpe:2.3:a:f5:nginx:1.24.0:*:*:*:*:*:*:*"
    }
  ]
}`

const FIELD_DOCS: { name: string; required: boolean; description: string }[] = [
  { name: 'canonicalName', required: true, description: 'Human-readable software name' },
  { name: 'canonicalProductKey', required: true, description: 'Unique product key (vendor:product)' },
  { name: 'detectedVersion', required: false, description: 'Detected version string' },
  { name: 'canonicalVendor', required: false, description: 'Vendor / publisher name' },
  { name: 'category', required: false, description: 'Software category (e.g. web-server)' },
  { name: 'primaryCpe23Uri', required: false, description: 'CPE 2.3 URI for vulnerability matching' },
]

const theme = EditorView.theme({
  '&': { fontSize: '13px' },
  '.cm-gutters': {
    backgroundColor: 'hsl(var(--muted))',
    borderRight: '1px solid hsl(var(--border))',
  },
})

export function ExpectedOutputPanel() {
  const extensions = useMemo(() => [theme, json(), EditorView.lineWrapping], [])

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Expected Output — DetectedSoftware</CardTitle>
        <p className="text-xs text-muted-foreground">
          Your script must print a single JSON object to stdout matching this schema.
        </p>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="overflow-hidden rounded-lg border">
          <CodeMirror
            value={EXPECTED_OUTPUT}
            extensions={extensions}
            readOnly
            height="220px"
            basicSetup={{ lineNumbers: true, foldGutter: true }}
          />
        </div>
        <div className="space-y-1.5">
          {FIELD_DOCS.map((field) => (
            <div key={field.name} className="flex items-center gap-2 text-sm">
              <Badge
                variant={field.required ? 'default' : 'secondary'}
                className={field.required ? 'bg-green-600 text-[10px]' : 'text-[10px]'}
              >
                {field.required ? 'required' : 'optional'}
              </Badge>
              <code className="text-xs font-medium">{field.name}</code>
              <span className="text-muted-foreground">{field.description}</span>
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  )
}
```

- [ ] **Step 3: Add toggle button in ScanningToolEditor**

In `ScanningToolEditor.tsx`:
- Add import: `import { ExpectedOutputPanel } from './ExpectedOutputPanel'`
- Add import: `import { FileJson } from 'lucide-react'`
- Add state: `const [showExpectedOutput, setShowExpectedOutput] = useState(false)`
- In the header bar (next to the Back button and tool name), add a toggle button:

```tsx
<Button
  variant="outline"
  size="sm"
  onClick={() => setShowExpectedOutput(!showExpectedOutput)}
>
  <FileJson className="mr-1 h-4 w-4" />
  {showExpectedOutput ? 'Hide' : 'Show'} Expected Output
</Button>
```

- Below the grid (after closing `</div>` of `lg:grid-cols-[1fr_300px]`), conditionally render:

```tsx
{showExpectedOutput && <ExpectedOutputPanel />}
```

- [ ] **Step 4: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit`

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/features/admin/scanning-tools/ExpectedOutputPanel.tsx frontend/src/components/features/admin/scanning-tools/ScanningToolEditor.tsx frontend/package.json frontend/package-lock.json
git commit -m "feat: add expected output JSON panel to scanning tool editor"
```

---

### Task 2: Cron schedule preview

**Files:**
- Modify: `frontend/src/components/features/admin/scan-profiles/ScanProfileDialog.tsx`

- [ ] **Step 1: Install cronstrue**

Run: `cd frontend && npm install cronstrue`

- [ ] **Step 2: Add cron preview to the dialog**

In `ScanProfileDialog.tsx`:
- Add import: `import cronstrue from 'cronstrue'`
- Replace the existing cron preview `<p>` tag (lines 102-106) with:

```tsx
{cronSchedule && (() => {
  try {
    return (
      <p className="text-muted-foreground text-xs mt-1">
        {cronstrue.toString(cronSchedule)}
      </p>
    )
  } catch {
    return (
      <p className="text-destructive text-xs mt-1">
        Invalid cron expression
      </p>
    )
  }
})()}
```

- [ ] **Step 3: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit`

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/features/admin/scan-profiles/ScanProfileDialog.tsx frontend/package.json frontend/package-lock.json
git commit -m "feat: add human-readable cron preview to scan profile dialog"
```

---

### Task 3: Drag-to-reorder tools in profile dialog

**Files:**
- Modify: `frontend/src/components/features/admin/scan-profiles/ScanProfileDialog.tsx`

- [ ] **Step 1: Install @dnd-kit packages**

Run: `cd frontend && npm install @dnd-kit/core @dnd-kit/sortable @dnd-kit/utilities`

- [ ] **Step 2: Add drag-to-reorder to the tools list**

In `ScanProfileDialog.tsx`:
- Add imports:

```tsx
import { DndContext, closestCenter, type DragEndEvent } from '@dnd-kit/core'
import { SortableContext, useSortable, verticalListSortingStrategy, arrayMove } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { GripVertical } from 'lucide-react'
```

- Create a `SortableToolItem` component inside the file:

```tsx
function SortableToolItem({
  tool,
  checked,
  onToggle,
}: {
  tool: ScanningTool
  checked: boolean
  onToggle: () => void
}) {
  const { attributes, listeners, setNodeRef, transform, transition } = useSortable({ id: tool.id })
  const style = { transform: CSS.Transform.toString(transform), transition }

  return (
    <div ref={setNodeRef} style={style} className="flex items-center gap-2 text-sm">
      {checked && (
        <button type="button" className="cursor-grab touch-none text-muted-foreground" {...attributes} {...listeners}>
          <GripVertical className="h-3.5 w-3.5" />
        </button>
      )}
      <label className="flex items-center gap-2 cursor-pointer flex-1">
        <input type="checkbox" checked={checked} onChange={onToggle} />
        {tool.name}
        <span className="text-muted-foreground">({tool.scriptType})</span>
      </label>
    </div>
  )
}
```

- Replace the Tools section (the `<div>` containing the tool checkboxes, lines 133-149) with:

```tsx
<div>
  <Label>Tools (drag to reorder)</Label>
  <div className="mt-1 space-y-2 rounded border p-2">
    {tools.length === 0 && (
      <p className="text-muted-foreground text-sm">No tools created yet.</p>
    )}
    <DndContext
      collisionDetection={closestCenter}
      onDragEnd={(event: DragEndEvent) => {
        const { active, over } = event
        if (over && active.id !== over.id) {
          setSelectedToolIds((prev) => {
            const oldIndex = prev.indexOf(String(active.id))
            const newIndex = prev.indexOf(String(over.id))
            return arrayMove(prev, oldIndex, newIndex)
          })
        }
      }}
    >
      <SortableContext items={selectedToolIds} strategy={verticalListSortingStrategy}>
        {/* Show selected tools first (sortable), then unselected */}
        {[
          ...tools.filter((t) => selectedToolIds.includes(t.id))
            .sort((a, b) => selectedToolIds.indexOf(a.id) - selectedToolIds.indexOf(b.id)),
          ...tools.filter((t) => !selectedToolIds.includes(t.id)),
        ].map((tool) => (
          <SortableToolItem
            key={tool.id}
            tool={tool}
            checked={selectedToolIds.includes(tool.id)}
            onToggle={() => toggleTool(tool.id)}
          />
        ))}
      </SortableContext>
    </DndContext>
  </div>
</div>
```

- [ ] **Step 3: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit`

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/features/admin/scan-profiles/ScanProfileDialog.tsx frontend/package.json frontend/package-lock.json
git commit -m "feat: add drag-to-reorder tool execution order in scan profile dialog"
```

---

### Task 4: Assigned devices drawer on scan profiles

**Files:**
- Modify: `frontend/src/api/authenticated-scans.schemas.ts`
- Modify: `frontend/src/api/authenticated-scans.functions.ts`
- Modify: `frontend/src/components/features/admin/scan-profiles/ScanProfilesTab.tsx`

- [ ] **Step 1: Add schema and server function**

Append to `authenticated-scans.schemas.ts`:

```typescript
// --- Scan Profile Assignments ---

export const profileAssignedDeviceSchema = z.object({
  assetId: z.string().uuid(),
  assetName: z.string(),
  assignedByRuleId: z.string().uuid().nullable(),
  assignedAt: isoDateTimeSchema,
})

export type ProfileAssignedDevice = z.infer<typeof profileAssignedDeviceSchema>
```

Append to `authenticated-scans.functions.ts` (add `profileAssignedDeviceSchema` to imports):

```typescript
// ─── Profile Assigned Devices ───

export const fetchProfileAssignedDevices = createServerFn({ method: 'GET' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ profileId: z.string().uuid() }))
  .handler(async ({ context, data: { profileId } }) => {
    return z.array(profileAssignedDeviceSchema).parse(
      await apiGet(`/scan-profiles/${profileId}/assigned-devices`, context),
    )
  })
```

- [ ] **Step 2: Add backend endpoint for assigned devices**

In `src/PatchHound.Api/Controllers/ScanProfilesController.cs`, add:

```csharp
public record AssignedDeviceDto(Guid AssetId, string AssetName, Guid? AssignedByRuleId, DateTimeOffset AssignedAt);

[HttpGet("{id:guid}/assigned-devices")]
public async Task<ActionResult<List<AssignedDeviceDto>>> GetAssignedDevices(Guid id, CancellationToken ct)
{
    var profile = await db.ScanProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
    if (profile is null) return NotFound();
    if (!tenantContext.HasAccessToTenant(profile.TenantId)) return Forbid();

    var assignments = await db.AssetScanProfileAssignments.AsNoTracking()
        .Where(a => a.ScanProfileId == id)
        .ToListAsync(ct);

    var assetIds = assignments.Select(a => a.AssetId).Distinct().ToList();
    var assetNames = await db.Assets.AsNoTracking()
        .Where(a => assetIds.Contains(a.Id))
        .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

    var items = assignments.Select(a => new AssignedDeviceDto(
        a.AssetId,
        assetNames.GetValueOrDefault(a.AssetId, "—"),
        a.AssignedByRuleId,
        a.AssignedAt)).ToList();

    return items;
}
```

- [ ] **Step 3: Add assigned devices drawer to ScanProfilesTab**

In `ScanProfilesTab.tsx`:
- Add imports:

```tsx
import { useQuery } from '@tanstack/react-query'
import { Eye } from 'lucide-react'
import { fetchProfileAssignedDevices } from '@/api/authenticated-scans.functions'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'
```

- Add state: `const [viewingProfileId, setViewingProfileId] = useState<string | null>(null)`
- Add the query:

```tsx
const devicesQuery = useQuery({
  queryKey: ['profile-assigned-devices', viewingProfileId],
  queryFn: () => fetchProfileAssignedDevices({ data: { profileId: viewingProfileId! } }),
  enabled: Boolean(viewingProfileId),
})
```

- Add a "View devices" button in the actions column (before the Edit button):

```tsx
<Button
  size="sm"
  variant="ghost"
  onClick={() => setViewingProfileId(row.original.id)}
  title="View assigned devices"
>
  <Eye className="h-3 w-3" />
</Button>
```

- Add the Sheet after the ScanProfileDialog:

```tsx
<Sheet open={Boolean(viewingProfileId)} onOpenChange={(open) => { if (!open) setViewingProfileId(null) }}>
  <SheetContent>
    <SheetHeader>
      <SheetTitle>Assigned Devices</SheetTitle>
      <SheetDescription>
        Devices assigned to this profile via asset rules.
      </SheetDescription>
    </SheetHeader>
    <div className="mt-4 space-y-2">
      {devicesQuery.isLoading && (
        <p className="text-sm text-muted-foreground">Loading...</p>
      )}
      {devicesQuery.data?.length === 0 && (
        <p className="text-sm text-muted-foreground">No devices assigned yet.</p>
      )}
      {devicesQuery.data?.map((device) => (
        <div key={device.assetId} className="flex items-center justify-between rounded border px-3 py-2 text-sm">
          <span className="font-mono">{device.assetName}</span>
          {device.assignedByRuleId && (
            <Badge variant="outline" className="text-[10px]">rule</Badge>
          )}
        </div>
      ))}
    </div>
  </SheetContent>
</Sheet>
```

- [ ] **Step 4: Check that Sheet component exists**

Run: `ls frontend/src/components/ui/sheet.tsx` — if it doesn't exist, use a Dialog instead with the same content.

- [ ] **Step 5: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit`

- [ ] **Step 6: Run backend tests**

Run: `dotnet test --nologo`

- [ ] **Step 7: Commit**

```bash
git add frontend/src/api/authenticated-scans.schemas.ts frontend/src/api/authenticated-scans.functions.ts frontend/src/components/features/admin/scan-profiles/ScanProfilesTab.tsx src/PatchHound.Api/Controllers/ScanProfilesController.cs
git commit -m "feat: add assigned devices drawer to scan profiles tab"
```

---

### Task 5: Full build verification

- [ ] **Step 1: TypeScript check**

Run: `cd frontend && npx tsc --noEmit`

- [ ] **Step 2: Frontend build**

Run: `cd frontend && npm run build`

- [ ] **Step 3: Backend test suite**

Run: `dotnet test --nologo`
