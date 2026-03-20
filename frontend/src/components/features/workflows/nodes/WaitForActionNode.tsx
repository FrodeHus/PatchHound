import { Handle, Position, type NodeProps } from '@xyflow/react'
import { Clock } from 'lucide-react'

export function WaitForActionNode({ data, selected }: NodeProps) {
  const d = data
  const timeout = d.timeoutHours as number | undefined
  return (
    <div className={`min-w-[160px] rounded-xl border px-4 py-3 text-sm ${
      selected ? 'border-orange-500 ring-2 ring-orange-500/30' : 'border-orange-500/50'
    } bg-orange-500/10`}>
      <Handle type="target" position={Position.Top} />
      <div className="flex items-center gap-2">
        <Clock className="size-4 text-orange-500" />
        <span className="font-medium">{d.label as string ?? 'Wait for Action'}</span>
      </div>
      {timeout ? (
        <div className="mt-1.5 text-[10px] text-muted-foreground">
          Timeout: <span className="text-foreground">{timeout}h</span>
        </div>
      ) : (
        <div className="mt-1.5 text-[10px] text-muted-foreground/60">Click to configure…</div>
      )}
      <Handle type="source" position={Position.Bottom} />
    </div>
  )
}
