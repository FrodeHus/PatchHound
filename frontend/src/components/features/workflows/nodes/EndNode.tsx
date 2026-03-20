import { Handle, Position, type NodeProps } from '@xyflow/react'

export function EndNode({ data }: NodeProps) {
  return (
    <div className="rounded-full border-2 border-red-500 bg-red-500/10 px-5 py-2 text-center text-sm font-medium">
      {data.label as string ?? 'End'}
      <Handle type="target" position={Position.Top} />
    </div>
  )
}
