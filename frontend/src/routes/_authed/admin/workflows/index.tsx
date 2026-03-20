import { createFileRoute, Link, useRouter } from '@tanstack/react-router'
import { useMutation, useQuery } from '@tanstack/react-query'
import { toast } from 'sonner'
import { Plus, Play, Archive, ChevronRight } from 'lucide-react'
import { fetchWorkflowDefinitions, publishWorkflowDefinition, archiveWorkflowDefinition } from '@/api/workflows.functions'
import type { WorkflowDefinitionItem } from '@/api/workflows.schemas'
import { baseListSearchSchema } from '@/routes/-list-search'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'

export const Route = createFileRoute('/_authed/admin/workflows/')({
  validateSearch: baseListSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: async ({ deps }) => {
    return fetchWorkflowDefinitions({ data: { page: deps.page, pageSize: deps.pageSize } })
  },
  component: WorkflowsPage,
})

function statusVariant(status: string) {
  switch (status) {
    case 'Published':
      return 'default' as const
    case 'Draft':
      return 'secondary' as const
    case 'Archived':
      return 'outline' as const
    default:
      return 'secondary' as const
  }
}

function triggerLabel(trigger: string) {
  const labels: Record<string, string> = {
    VulnerabilityDetected: 'Vuln Detected',
    VulnerabilityReopened: 'Vuln Reopened',
    AssetOnboarded: 'Asset Onboarded',
    ScheduledIngestion: 'Scheduled Ingestion',
    ManualRun: 'Manual',
  }
  return labels[trigger] ?? trigger
}

function WorkflowsPage() {
  const router = useRouter()
  const data = Route.useLoaderData()
  const search = Route.useSearch()

  const definitionsQuery = useQuery({
    queryKey: ['workflow-definitions', search.page, search.pageSize],
    queryFn: () => fetchWorkflowDefinitions({ data: { page: search.page, pageSize: search.pageSize } }),
    initialData: data,
  })

  const publishMutation = useMutation({
    mutationFn: async (id: string) => {
      await publishWorkflowDefinition({ data: { id } })
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
    mutationFn: async (id: string) => {
      await archiveWorkflowDefinition({ data: { id } })
    },
    onSuccess: () => {
      toast.success('Workflow archived')
      void router.invalidate()
    },
    onError: () => {
      toast.error('Failed to archive workflow')
    },
  })

  const definitions = definitionsQuery.data?.items ?? []

  return (
    <section className="space-y-5">
      <div className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-2">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Administration</p>
            <h1 className="text-3xl font-semibold tracking-[-0.04em]">Workflows</h1>
            <p className="max-w-2xl text-sm text-muted-foreground">
              Design and manage workflows that automate vulnerability triage, assignment routing, and human-in-the-loop approvals.
            </p>
          </div>
          <Link to="/admin/workflows/new">
            <Button>
              <Plus className="mr-2 size-4" />
              New Workflow
            </Button>
          </Link>
        </div>
      </div>

      <Card className="rounded-2xl border-border/70">
        <CardHeader>
          <CardTitle>Workflow Definitions</CardTitle>
        </CardHeader>
        <CardContent>
          {definitions.length === 0 ? (
            <p className="py-8 text-center text-sm text-muted-foreground">
              No workflows defined yet. Create one to get started.
            </p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Scope</TableHead>
                  <TableHead>Trigger</TableHead>
                  <TableHead>Version</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Updated</TableHead>
                  <TableHead className="w-[120px]">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {definitions.map((def: WorkflowDefinitionItem) => (
                  <TableRow key={def.id}>
                    <TableCell>
                      <Link
                        to="/admin/workflows/$id"
                        params={{ id: def.id }}
                        className="font-medium text-primary hover:underline"
                      >
                        {def.name}
                      </Link>
                    </TableCell>
                    <TableCell>
                      <Badge variant="outline">{def.scope}</Badge>
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {triggerLabel(def.triggerType)}
                    </TableCell>
                    <TableCell className="text-sm">v{def.version}</TableCell>
                    <TableCell>
                      <Badge variant={statusVariant(def.status)}>{def.status}</Badge>
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {new Date(def.updatedAt).toLocaleDateString()}
                    </TableCell>
                    <TableCell>
                      <div className="flex items-center gap-1">
                        {def.status === 'Draft' && (
                          <Button
                            variant="ghost"
                            size="icon"
                            onClick={() => publishMutation.mutate(def.id)}
                            title="Publish"
                          >
                            <Play className="size-4" />
                          </Button>
                        )}
                        {def.status !== 'Archived' && (
                          <Button
                            variant="ghost"
                            size="icon"
                            onClick={() => archiveMutation.mutate(def.id)}
                            title="Archive"
                          >
                            <Archive className="size-4" />
                          </Button>
                        )}
                        <Link to="/admin/workflows/$id" params={{ id: def.id }}>
                          <Button variant="ghost" size="icon" title="Edit">
                            <ChevronRight className="size-4" />
                          </Button>
                        </Link>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </section>
  )
}
