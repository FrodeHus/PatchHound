import { useMemo, useState } from 'react'
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  Panel,
  type Node,
  type OnSelectionChangeParams,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { X } from 'lucide-react'
import type { WorkflowNodeExecution } from '@/api/workflows.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { nodeTypeMap } from './nodeTypes'
import type { WorkflowGraph } from './WorkflowDesigner'

type WorkflowRunViewerProps = {
  graph: WorkflowGraph
  nodeExecutions: WorkflowNodeExecution[]
}

const statusColors: Record<string, { outline: string; extra?: string }> = {
  Completed: { outline: '3px solid oklch(0.72 0.19 142)' },
  Running: { outline: '3px solid oklch(0.65 0.19 250)', extra: 'pulse' },
  WaitingForAction: { outline: '3px solid oklch(0.78 0.16 75)' },
  Failed: { outline: '3px solid oklch(0.63 0.24 25)' },
  Skipped: { outline: '2px dashed oklch(0.55 0 0)' },
}

const statusBadgeVariant: Record<string, 'default' | 'secondary' | 'destructive' | 'outline'> = {
  Completed: 'default',
  Running: 'secondary',
  WaitingForAction: 'outline',
  Failed: 'destructive',
  Skipped: 'outline',
  Pending: 'outline',
}

const legendItems = [
  { label: 'Completed', color: 'oklch(0.72 0.19 142)' },
  { label: 'Running', color: 'oklch(0.65 0.19 250)' },
  { label: 'Waiting', color: 'oklch(0.78 0.16 75)' },
  { label: 'Failed', color: 'oklch(0.63 0.24 25)' },
  { label: 'Skipped', color: 'oklch(0.55 0 0)' },
]

export function WorkflowRunViewer({ graph, nodeExecutions }: WorkflowRunViewerProps) {
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)

  const executionMap = useMemo(
    () => new Map(nodeExecutions.map((e) => [e.nodeId, e])),
    [nodeExecutions],
  )

  const styledNodes = useMemo(
    () =>
      graph.nodes.map((node) => {
        const exec = executionMap.get(node.id)
        const status = exec?.status
        const colors = status ? statusColors[status] : undefined
        return {
          ...node,
          style: {
            ...(colors ? { outline: colors.outline, outlineOffset: '2px' } : { opacity: 0.4 }),
            ...(colors?.extra === 'pulse' ? { animation: 'pulse 2s infinite' } : {}),
          },
        } as Node
      }),
    [graph.nodes, executionMap],
  )

  const handleSelectionChange = ({ nodes }: OnSelectionChangeParams) => {
    setSelectedNodeId(nodes.length === 1 ? nodes[0].id : null)
  }

  const selectedExec = selectedNodeId ? executionMap.get(selectedNodeId) : null
  const selectedNode = selectedNodeId ? graph.nodes.find((n) => n.id === selectedNodeId) : null

  return (
    <div className="flex h-[calc(100vh-24rem)] overflow-hidden rounded-2xl border border-border/70">
      <div className="flex-1">
        <ReactFlow
          nodes={styledNodes}
          edges={graph.edges}
          nodeTypes={nodeTypeMap}
          fitView
          nodesDraggable={false}
          nodesConnectable={false}
          elementsSelectable
          onSelectionChange={handleSelectionChange}
        >
          <Background />
          <Controls />
          <MiniMap />
          <Panel position="top-left">
            <div className="flex flex-wrap gap-2 rounded-xl border border-border/60 bg-card/92 px-3 py-2">
              {legendItems.map((item) => (
                <div key={item.label} className="flex items-center gap-1.5 text-[10px] text-muted-foreground">
                  <span
                    className="inline-block size-2.5 rounded-full"
                    style={{ backgroundColor: item.color }}
                  />
                  {item.label}
                </div>
              ))}
            </div>
          </Panel>
        </ReactFlow>
      </div>
      {selectedExec && selectedNode && (
        <NodeExecutionDetail
          node={selectedNode}
          execution={selectedExec}
          onClose={() => setSelectedNodeId(null)}
        />
      )}
    </div>
  )
}

function NodeExecutionDetail({
  node,
  execution,
  onClose,
}: {
  node: Node
  execution: WorkflowNodeExecution
  onClose: () => void
}) {
  return (
    <div className="w-80 shrink-0 space-y-4 overflow-y-auto border-l border-border/60 bg-card/60 p-4">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold">{node.data?.label ?? node.type}</h3>
        <Button variant="ghost" size="icon" className="size-7" onClick={onClose}>
          <X className="size-3.5" />
        </Button>
      </div>

      <div className="space-y-3">
        <DetailRow label="Node Type">{execution.nodeType}</DetailRow>
        <DetailRow label="Status">
          <Badge variant={statusBadgeVariant[execution.status] ?? 'outline'}>
            {execution.status}
          </Badge>
        </DetailRow>
        <DetailRow label="Started">
          {execution.startedAt ? new Date(execution.startedAt).toLocaleString() : '—'}
        </DetailRow>
        <DetailRow label="Completed">
          {execution.completedAt ? new Date(execution.completedAt).toLocaleString() : '—'}
        </DetailRow>
        {execution.assignedTeamId && (
          <DetailRow label="Team ID">
            <span className="font-mono text-[10px]">{execution.assignedTeamId}</span>
          </DetailRow>
        )}
        {execution.completedByUserId && (
          <DetailRow label="Completed By">
            <span className="font-mono text-[10px]">{execution.completedByUserId}</span>
          </DetailRow>
        )}
        {execution.error && (
          <div className="space-y-1">
            <p className="text-[10px] uppercase tracking-wider text-destructive">Error</p>
            <p className="text-xs text-destructive">{execution.error}</p>
          </div>
        )}
        {execution.inputJson && (
          <JsonBlock label="Input" json={execution.inputJson} />
        )}
        {execution.outputJson && (
          <JsonBlock label="Output" json={execution.outputJson} />
        )}
      </div>
    </div>
  )
}

function DetailRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-0.5">
      <p className="text-[10px] uppercase tracking-wider text-muted-foreground">{label}</p>
      <div className="text-sm">{children}</div>
    </div>
  )
}

function JsonBlock({ label, json }: { label: string; json: string }) {
  let formatted: string
  try {
    formatted = JSON.stringify(JSON.parse(json), null, 2)
  } catch {
    formatted = json
  }

  return (
    <div className="space-y-1">
      <p className="text-[10px] uppercase tracking-wider text-muted-foreground">{label}</p>
      <pre className="max-h-40 overflow-auto rounded-lg border border-border/60 bg-background/50 p-2 text-[10px]">
        {formatted}
      </pre>
    </div>
  )
}
