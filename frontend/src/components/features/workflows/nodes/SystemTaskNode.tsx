import { Handle, Position, type NodeProps } from '@xyflow/react'
import { Cog } from 'lucide-react'

export function SystemTaskNode({ data, selected }: NodeProps) {
  const d = data
  const taskType = d.taskType as string | undefined
  return (
    <div className={`min-w-[160px] rounded-xl border px-4 py-3 text-sm ${
      selected ? 'border-cyan-500 ring-2 ring-cyan-500/30' : 'border-cyan-500/50'
    } bg-cyan-500/10`}>
      <Handle type="target" position={Position.Top} />
      <div className="flex items-center gap-2">
        <Cog className="size-4 text-cyan-500" />
        <span className="font-medium">{d.label as string ?? 'System Task'}</span>
      </div>
      {taskType ? (
        <div className="mt-1.5 text-[10px] text-muted-foreground">
          Type: <span className="text-foreground">{taskType}</span>
        </div>
      ) : (
        <div className="mt-1.5 text-[10px] text-muted-foreground/60">Click to configure…</div>
      )}
      <Handle type="source" position={Position.Bottom} />
    </div>
  )
}
