import { Handle, Position, type NodeProps } from '@xyflow/react'

export function StartNode({ data }: NodeProps) {
  return (
    <div className="rounded-full border-2 border-green-500 bg-green-500/10 px-5 py-2 text-center text-sm font-medium">
      {data.label as string ?? 'Start'}
      <Handle type="source" position={Position.Bottom} />
    </div>
  )
}
