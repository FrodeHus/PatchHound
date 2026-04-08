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
import cronstrue from 'cronstrue'
import { DndContext, closestCenter, type DragEndEvent } from '@dnd-kit/core'
import { SortableContext, useSortable, verticalListSortingStrategy, arrayMove } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { GripVertical } from 'lucide-react'
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
      <DialogContent size="lg">
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
          </div>
          <div>
            <Label>Runner</Label>
            <Select value={scanRunnerId} onValueChange={(v) => v && setScanRunnerId(v)}>
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
            <Select value={connectionProfileId} onValueChange={(v) => v && setConnectionProfileId(v)}>
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
