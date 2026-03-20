import { Handle, Position, type NodeProps } from '@xyflow/react'
import { Merge } from 'lucide-react'

export function MergeNode({ data, selected }: NodeProps) {
  const d = data
  const mode = (d.waitMode as string) ?? 'all'
  return (
    <div className={`min-w-[120px] rounded-xl border px-4 py-3 text-center text-sm ${
      selected ? 'border-slate-500 ring-2 ring-slate-500/30' : 'border-slate-500/50'
    } bg-slate-500/10`}>
      <Handle type="target" position={Position.Top} />
      <div className="flex items-center justify-center gap-2">
        <Merge className="size-4 text-slate-500" />
        <span className="font-medium">{d.label as string ?? 'Merge'}</span>
      </div>
      <div className="mt-1 text-[10px] text-muted-foreground">
        Wait: <span className="text-foreground">{mode === 'any' ? 'Any branch' : 'All branches'}</span>
      </div>
      <Handle type="source" position={Position.Bottom} />
    </div>
  )
}
