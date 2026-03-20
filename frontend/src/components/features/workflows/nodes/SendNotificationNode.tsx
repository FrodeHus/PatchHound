import { Handle, Position, type NodeProps } from '@xyflow/react'
import { Bell } from 'lucide-react'

export function SendNotificationNode({ data, selected }: NodeProps) {
  const d = data
  const channel = d.channel as string | undefined
  const teamName = d.teamName as string | undefined
  const hasConfig = !!d.teamId
  return (
    <div className={`min-w-[160px] rounded-xl border px-4 py-3 text-sm ${
      selected ? 'border-purple-500 ring-2 ring-purple-500/30' : 'border-purple-500/50'
    } bg-purple-500/10`}>
      <Handle type="target" position={Position.Top} />
      <div className="flex items-center gap-2">
        <Bell className="size-4 text-purple-500" />
        <span className="font-medium">{d.label as string ?? 'Send Notification'}</span>
      </div>
      {hasConfig ? (
        <div className="mt-1.5 space-y-0.5 text-[10px] text-muted-foreground">
          <div>Team: <span className="text-foreground">{teamName ?? (d.teamId as string)?.slice(0, 8)}</span></div>
          {channel && <div>Via: <span className="text-foreground">{channel}</span></div>}
        </div>
      ) : (
        <div className="mt-1.5 text-[10px] text-muted-foreground/60">Click to configure…</div>
      )}
      <Handle type="source" position={Position.Bottom} />
    </div>
  )
}
