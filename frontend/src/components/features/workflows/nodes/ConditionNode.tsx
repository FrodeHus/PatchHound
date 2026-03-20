import { Handle, Position, type NodeProps } from '@xyflow/react'
import { GitBranch } from 'lucide-react'

type ConditionRule = { field: string; operator: string; value: string }

export function ConditionNode({ data, selected }: NodeProps) {
  const d = data
  const rules = (d.rules as ConditionRule[] | undefined) ?? []
  return (
    <div className={`min-w-[160px] rounded-xl border px-4 py-3 text-sm ${
      selected ? 'border-amber-500 ring-2 ring-amber-500/30' : 'border-amber-500/50'
    } bg-amber-500/10`}>
      <Handle type="target" position={Position.Top} />
      <div className="flex items-center gap-2">
        <GitBranch className="size-4 text-amber-500" />
        <span className="font-medium">{d.label as string ?? 'Condition'}</span>
      </div>
      {rules.length > 0 ? (
        <div className="mt-1.5 space-y-0.5 text-[10px] text-muted-foreground">
          {rules.map((r, i) => (
            <div key={i}>{r.field} <span className="text-foreground">{r.operator}</span> {r.value}</div>
          ))}
        </div>
      ) : (
        <div className="mt-1.5 text-[10px] text-muted-foreground/60">Click to add rules…</div>
      )}
      <div className="mt-1 flex justify-between px-2 text-[9px] text-muted-foreground">
        <span>✓ true</span>
        <span>✗ false</span>
      </div>
      <Handle type="source" position={Position.Bottom} id="true" style={{ left: '30%' }} />
      <Handle type="source" position={Position.Bottom} id="false" style={{ left: '70%' }} />
    </div>
  )
}
