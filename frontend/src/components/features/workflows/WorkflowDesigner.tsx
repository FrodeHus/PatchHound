import { useCallback, useMemo, useState } from 'react'
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  addEdge,
  useNodesState,
  useEdgesState,
  type Connection,
  type Node,
  type Edge,
  type NodeTypes,
  Panel,
  MarkerType,
  type OnSelectionChangeParams,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { Button } from '@/components/ui/button'
import { StartNode } from './nodes/StartNode'
import { EndNode } from './nodes/EndNode'
import { AssignGroupNode } from './nodes/AssignGroupNode'
import { ConditionNode } from './nodes/ConditionNode'
import { SendNotificationNode } from './nodes/SendNotificationNode'
import { MergeNode } from './nodes/MergeNode'
import { SystemTaskNode } from './nodes/SystemTaskNode'
import { WaitForActionNode } from './nodes/WaitForActionNode'
import { NodeConfigPanel } from './NodeConfigPanel'
import type { TeamItem } from '@/api/teams.schemas'

export type WorkflowGraph = {
  nodes: Node[]
  edges: Edge[]
}

type WorkflowDesignerProps = {
  initialGraph?: WorkflowGraph
  onSave: (graph: WorkflowGraph) => void
  readOnly?: boolean
  teams?: TeamItem[]
}

const defaultGraph: WorkflowGraph = {
  nodes: [
    { id: 'start', type: 'Start', position: { x: 250, y: 50 }, data: { label: 'Start' } },
    { id: 'end', type: 'End', position: { x: 250, y: 400 }, data: { label: 'End' } },
  ],
  edges: [],
}

const nodeTypeMap: NodeTypes = {
  Start: StartNode,
  End: EndNode,
  AssignGroup: AssignGroupNode,
  Condition: ConditionNode,
  SendNotification: SendNotificationNode,
  Merge: MergeNode,
  SystemTask: SystemTaskNode,
  WaitForAction: WaitForActionNode,
}

const paletteItems = [
  { type: 'AssignGroup', label: 'Assign Group' },
  { type: 'WaitForAction', label: 'Wait for Action' },
  { type: 'SendNotification', label: 'Send Notification' },
  { type: 'Condition', label: 'Condition' },
  { type: 'Merge', label: 'Merge' },
  { type: 'SystemTask', label: 'System Task' },
]

let idCounter = 0
function nextId() {
  return `node_${Date.now()}_${idCounter++}`
}

export function WorkflowDesigner({ initialGraph, onSave, readOnly = false, teams = [] }: WorkflowDesignerProps) {
  const initial = initialGraph ?? defaultGraph
  const [nodes, setNodes, onNodesChange] = useNodesState(initial.nodes)
  const [edges, setEdges, onEdgesChange] = useEdgesState(initial.edges)
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)

  const selectedNode = useMemo(
    () => nodes.find((n) => n.id === selectedNodeId) ?? null,
    [nodes, selectedNodeId],
  )

  const onConnect = useCallback(
    (params: Connection) => {
      setEdges((eds) =>
        addEdge(
          { ...params, markerEnd: { type: MarkerType.ArrowClosed }, animated: true },
          eds,
        ),
      )
    },
    [setEdges],
  )

  const addNode = useCallback(
    (type: string) => {
      const id = nextId()
      const newNode: Node = {
        id,
        type,
        position: { x: 250, y: 200 + nodes.length * 80 },
        data: { label: type.replace(/([A-Z])/g, ' $1').trim() },
      }
      setNodes((nds) => [...nds, newNode])
    },
    [nodes.length, setNodes],
  )

  const onSelectionChange = useCallback(
    ({ nodes: selected }: OnSelectionChangeParams) => {
      if (selected.length === 1 && !readOnly) {
        setSelectedNodeId(selected[0].id)
      } else {
        setSelectedNodeId(null)
      }
    },
    [readOnly],
  )

  const updateNodeData = useCallback(
    (nodeId: string, data: Record<string, unknown>) => {
      setNodes((nds) =>
        nds.map((n) => (n.id === nodeId ? { ...n, data } : n)),
      )
    },
    [setNodes],
  )

  const handleSave = useCallback(() => {
    onSave({ nodes, edges })
  }, [nodes, edges, onSave])

  return (
    <div className="flex h-[calc(100vh-16rem)] overflow-hidden rounded-2xl border border-border/70">
      {!readOnly && (
        <div className="w-52 shrink-0 space-y-2 border-r border-border/60 bg-card/60 p-3">
          <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
            Node Palette
          </p>
          {paletteItems.map((item) => (
            <Button
              key={item.type}
              variant="outline"
              size="sm"
              className="w-full justify-start text-xs"
              onClick={() => addNode(item.type)}
            >
              {item.label}
            </Button>
          ))}
        </div>
      )}
      <div className="flex-1">
        <ReactFlow
          nodes={nodes}
          edges={edges}
          onNodesChange={readOnly ? undefined : onNodesChange}
          onEdgesChange={readOnly ? undefined : onEdgesChange}
          onConnect={readOnly ? undefined : onConnect}
          onSelectionChange={onSelectionChange}
          nodeTypes={nodeTypeMap}
          fitView
          nodesDraggable={!readOnly}
          nodesConnectable={!readOnly}
          elementsSelectable={!readOnly}
          defaultEdgeOptions={{ markerEnd: { type: MarkerType.ArrowClosed }, animated: true }}
        >
          <Background />
          <Controls />
          <MiniMap />
          {!readOnly && (
            <Panel position="top-right">
              <Button size="sm" onClick={handleSave}>
                Save Graph
              </Button>
            </Panel>
          )}
        </ReactFlow>
      </div>
      {selectedNode && !readOnly && (
        <div className="w-72 shrink-0 border-l border-border/60 bg-card/60">
          <NodeConfigPanel
            node={selectedNode}
            teams={teams}
            onUpdate={updateNodeData}
            onClose={() => setSelectedNodeId(null)}
          />
        </div>
      )}
    </div>
  )
}
