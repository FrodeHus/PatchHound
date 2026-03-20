import { useState } from 'react'
import { createFileRoute, Link, useRouter } from '@tanstack/react-router'
import { useMutation, useQuery } from '@tanstack/react-query'
import { toast } from 'sonner'
import { ArrowLeft, Play, Archive } from 'lucide-react'
import {
  fetchWorkflowDefinition,
  updateWorkflowDefinition,
  publishWorkflowDefinition,
  archiveWorkflowDefinition,
  fetchWorkflowInstances,
} from '@/api/workflows.functions'
import { fetchTeams } from '@/api/teams.functions'
import type { WorkflowInstanceItem } from '@/api/workflows.schemas'
import { WorkflowDesigner, type WorkflowGraph } from '@/components/features/workflows/WorkflowDesigner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'

export const Route = createFileRoute('/_authed/admin/workflows/$id')({
  loader: async ({ params }) => {
    const [definition, instances, teams] = await Promise.all([
      fetchWorkflowDefinition({ data: { id: params.id } }),
      fetchWorkflowInstances({ data: { definitionId: params.id, page: 1, pageSize: 20 } }),
      fetchTeams({ data: { pageSize: 200 } }),
    ])
    return { definition, instances, teams }
  },
  component: WorkflowDetailPage,
})

function instanceStatusVariant(status: string) {
  switch (status) {
    case 'Completed':
      return 'default' as const
    case 'Running':
      return 'secondary' as const
    case 'WaitingForAction':
      return 'outline' as const
    case 'Failed':
      return 'destructive' as const
    case 'Cancelled':
      return 'outline' as const
    default:
      return 'secondary' as const
  }
}

function WorkflowDetailPage() {
  const router = useRouter()
  const { definition, instances, teams } = Route.useLoaderData()

  const [name, setName] = useState(definition.name)
  const [description, setDescription] = useState(definition.description ?? '')

  const initialGraph: WorkflowGraph = (() => {
    try {
      return JSON.parse(definition.graphJson) as WorkflowGraph
    } catch {
      return { nodes: [], edges: [] }
    }
  })()

  const isArchived = definition.status === 'Archived'

  const instancesQuery = useQuery({
    queryKey: ['workflow-instances', definition.id],
    queryFn: () => fetchWorkflowInstances({ data: { definitionId: definition.id, page: 1, pageSize: 20 } }),
    initialData: instances,
  })

  const updateMutation = useMutation({
    mutationFn: async (graph: WorkflowGraph) => {
      return updateWorkflowDefinition({
        data: {
          id: definition.id,
          name,
          description: description || null,
          graphJson: JSON.stringify(graph),
        },
      })
    },
    onSuccess: () => {
      toast.success('Workflow saved')
      void router.invalidate()
    },
    onError: () => {
      toast.error('Failed to save workflow')
    },
  })

  const publishMutation = useMutation({
    mutationFn: async () => {
      await publishWorkflowDefinition({ data: { id: definition.id } })
    },
    onSuccess: () => {
      toast.success('Workflow published')
      void router.invalidate()
    },
    onError: () => {
      toast.error('Failed to publish workflow')
    },
  })

  const archiveMutation = useMutation({
    mutationFn: async () => {
      await archiveWorkflowDefinition({ data: { id: definition.id } })
    },
    onSuccess: () => {
      toast.success('Workflow archived')
      void router.invalidate()
    },
    onError: () => {
      toast.error('Failed to archive workflow')
    },
  })

  return (
    <section className="space-y-5">
      <div className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-2">
            <Link to="/admin/workflows" search={{ page: 1, pageSize: 25 }} className="mb-2 flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground">
              <ArrowLeft className="size-3" /> Back to Workflows
            </Link>
            <h1 className="text-3xl font-semibold tracking-[-0.04em]">{definition.name}</h1>
            <div className="flex items-center gap-2">
              <Badge variant={definition.status === 'Published' ? 'default' : 'secondary'}>
                {definition.status}
              </Badge>
              <span className="text-sm text-muted-foreground">
                v{definition.version} &middot; {definition.scope} &middot; {definition.triggerType}
              </span>
            </div>
          </div>
          <div className="flex items-center gap-2">
            {definition.status === 'Draft' && (
              <Button variant="outline" size="sm" onClick={() => publishMutation.mutate()}>
                <Play className="mr-1 size-3" /> Publish
              </Button>
            )}
            {!isArchived && (
              <Button variant="outline" size="sm" onClick={() => archiveMutation.mutate()}>
                <Archive className="mr-1 size-3" /> Archive
              </Button>
            )}
          </div>
        </div>
      </div>

      <Tabs defaultValue="designer">
        <TabsList>
          <TabsTrigger value="designer">Designer</TabsTrigger>
          <TabsTrigger value="runs">
            Runs ({instancesQuery.data?.totalCount ?? 0})
          </TabsTrigger>
        </TabsList>

        <TabsContent value="designer" className="mt-4 space-y-4">
          {!isArchived && (
            <div className="grid gap-4 md:grid-cols-4">
              <div className="space-y-3">
                <div>
                  <label className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                    Name
                  </label>
                  <Input value={name} onChange={(e) => setName(e.target.value)} />
                </div>
                <div>
                  <label className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
                    Description
                  </label>
                  <Textarea
                    value={description}
                    onChange={(e) => setDescription(e.target.value)}
                    rows={3}
                  />
                </div>
              </div>
              <div className="md:col-span-3">
                <WorkflowDesigner
                  initialGraph={initialGraph}
                  onSave={(graph) => updateMutation.mutate(graph)}
                  teams={teams.items}
                />
              </div>
            </div>
          )}
          {isArchived && (
            <WorkflowDesigner initialGraph={initialGraph} onSave={() => {}} readOnly />
          )}
        </TabsContent>

        <TabsContent value="runs" className="mt-4">
          <Card className="rounded-2xl border-border/70">
            <CardHeader>
              <CardTitle>Workflow Runs</CardTitle>
            </CardHeader>
            <CardContent>
              {(instancesQuery.data?.items?.length ?? 0) === 0 ? (
                <p className="py-6 text-center text-sm text-muted-foreground">
                  No runs yet for this workflow.
                </p>
              ) : (
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Instance ID</TableHead>
                      <TableHead>Version</TableHead>
                      <TableHead>Status</TableHead>
                      <TableHead>Started</TableHead>
                      <TableHead>Completed</TableHead>
                      <TableHead>Error</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {instancesQuery.data?.items.map((inst: WorkflowInstanceItem) => (
                      <TableRow key={inst.id}>
                        <TableCell className="font-mono text-xs">
                          {inst.id.slice(0, 8)}...
                        </TableCell>
                        <TableCell>v{inst.definitionVersion}</TableCell>
                        <TableCell>
                          <Badge variant={instanceStatusVariant(inst.status)}>
                            {inst.status}
                          </Badge>
                        </TableCell>
                        <TableCell className="text-sm text-muted-foreground">
                          {new Date(inst.startedAt).toLocaleString()}
                        </TableCell>
                        <TableCell className="text-sm text-muted-foreground">
                          {inst.completedAt ? new Date(inst.completedAt).toLocaleString() : '—'}
                        </TableCell>
                        <TableCell className="max-w-[200px] truncate text-sm text-destructive">
                          {inst.error ?? '—'}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </CardContent>
          </Card>
        </TabsContent>
      </Tabs>
    </section>
  )
}
