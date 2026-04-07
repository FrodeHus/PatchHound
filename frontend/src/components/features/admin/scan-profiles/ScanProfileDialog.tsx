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
