import { Handle, Position, type NodeProps } from '@xyflow/react'
import { Users } from 'lucide-react'

export function AssignGroupNode({ data, selected }: NodeProps) {
  const d = data
  const teamName = d.teamName as string | undefined
  const action = d.requiredAction as string | undefined
  const hasConfig = !!d.teamId
  return (
    <div className={`min-w-[160px] rounded-xl border px-4 py-3 text-sm ${
      selected ? 'border-blue-500 ring-2 ring-blue-500/30' : 'border-blue-500/50'
    } bg-blue-500/10`}>
      <Handle type="target" position={Position.Top} />
      <div className="flex items-center gap-2">
        <Users className="size-4 text-blue-500" />
        <span className="font-medium">{d.label as string ?? 'Assign Group'}</span>
      </div>
      {hasConfig ? (
        <div className="mt-1.5 space-y-0.5 text-[10px] text-muted-foreground">
          <div>Team: <span className="text-foreground">{teamName ?? (d.teamId as string)?.slice(0, 8)}</span></div>
          {action && <div>Action: <span className="text-foreground">{action}</span></div>}
        </div>
      ) : (
        <div className="mt-1.5 text-[10px] text-muted-foreground/60">Click to configure…</div>
      )}
      <Handle type="source" position={Position.Bottom} />
    </div>
  )
}
