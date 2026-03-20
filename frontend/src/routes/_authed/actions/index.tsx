import { useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { Check, XCircle } from 'lucide-react'
import {
  fetchMyWorkflowActions,
  completeWorkflowAction,
  rejectWorkflowAction,
} from '@/api/workflows.functions'
import type { WorkflowActionItem } from '@/api/workflows.schemas'
import { baseListSearchSchema, searchStringSchema } from '@/routes/-list-search'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Textarea } from '@/components/ui/textarea'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { PaginationControls } from '@/components/ui/pagination-controls'

const actionsSearchSchema = baseListSearchSchema.extend({
  status: searchStringSchema,
})

export const Route = createFileRoute('/_authed/actions/')({
  validateSearch: actionsSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) =>
    fetchMyWorkflowActions({
      data: {
        status: deps.status || undefined,
        page: deps.page,
        pageSize: deps.pageSize,
      },
    }),
  component: WorkflowActionsPage,
})

function actionTypeBadgeColor(type: string) {
  switch (type) {
    case 'Review':
      return 'border-blue-500/30 bg-blue-500/10 text-blue-600'
    case 'FillForm':
      return 'border-amber-500/30 bg-amber-500/10 text-amber-600'
    case 'QA':
      return 'border-purple-500/30 bg-purple-500/10 text-purple-600'
    default:
      return 'border-border/70 bg-background/70'
  }
}

function actionStatusVariant(status: string) {
  switch (status) {
    case 'Pending':
      return 'outline' as const
    case 'Completed':
      return 'default' as const
    case 'Rejected':
      return 'destructive' as const
    case 'TimedOut':
      return 'secondary' as const
    default:
      return 'outline' as const
  }
}

function WorkflowActionsPage() {
  const data = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const queryClient = useQueryClient()
  const [expandedId, setExpandedId] = useState<string | null>(null)
  const [response, setResponse] = useState('')

  const actionsQuery = useQuery({
    queryKey: ['my-workflow-actions', search.status, search.page, search.pageSize],
    queryFn: () =>
      fetchMyWorkflowActions({
        data: {
          status: search.status || undefined,
          page: search.page,
          pageSize: search.pageSize,
        },
      }),
    initialData: data,
  })

  const completeMutation = useMutation({
    mutationFn: async ({ id, responseJson }: { id: string; responseJson: string | null }) => {
      await completeWorkflowAction({ data: { id, responseJson } })
    },
    onSuccess: () => {
      toast.success('Action completed')
      setExpandedId(null)
      setResponse('')
      void queryClient.invalidateQueries({ queryKey: ['my-workflow-actions'] })
    },
    onError: () => {
      toast.error('Failed to complete action')
    },
  })

  const rejectMutation = useMutation({
    mutationFn: async ({ id, responseJson }: { id: string; responseJson: string | null }) => {
      await rejectWorkflowAction({ data: { id, responseJson } })
    },
    onSuccess: () => {
      toast.success('Action rejected')
      setExpandedId(null)
      setResponse('')
      void queryClient.invalidateQueries({ queryKey: ['my-workflow-actions'] })
    },
    onError: () => {
      toast.error('Failed to reject action')
    },
  })

  const actions = actionsQuery.data?.items ?? []
  const isSubmitting = completeMutation.isPending || rejectMutation.isPending

  return (
    <section className="space-y-5">
      <div className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
        <div className="space-y-2">
          <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Workflow</p>
          <h1 className="text-3xl font-semibold tracking-[-0.04em]">Actions</h1>
          <p className="max-w-2xl text-sm text-muted-foreground">
            Review, approve, and complete workflow tasks assigned to your teams.
          </p>
        </div>
      </div>

      <div className="flex items-center gap-3">
        <Select
          value={search.status || 'all'}
          onValueChange={(value) => {
            void navigate({
              search: (prev) => ({ ...prev, status: value === 'all' ? '' : value, page: 1 }) as typeof prev,
            })
          }}
        >
          <SelectTrigger className="h-10 w-[180px] rounded-xl border-border/70 bg-background/80">
            <SelectValue placeholder="Filter by status" />
          </SelectTrigger>
          <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
            <SelectItem value="all">All statuses</SelectItem>
            <SelectItem value="Pending">Pending</SelectItem>
            <SelectItem value="Completed">Completed</SelectItem>
            <SelectItem value="Rejected">Rejected</SelectItem>
            <SelectItem value="TimedOut">Timed Out</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <Card className="rounded-2xl border-border/70">
        <CardHeader>
          <CardTitle>My Workflow Actions</CardTitle>
        </CardHeader>
        <CardContent>
          {actions.length === 0 ? (
            <p className="py-8 text-center text-sm text-muted-foreground">
              No workflow actions assigned to your teams right now.
            </p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Workflow</TableHead>
                  <TableHead>Type</TableHead>
                  <TableHead>Instructions</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Due</TableHead>
                  <TableHead>Created</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {actions.map((action: WorkflowActionItem) => (
                  <ActionRow
                    key={action.id}
                    action={action}
                    isExpanded={expandedId === action.id}
                    response={expandedId === action.id ? response : ''}
                    isSubmitting={isSubmitting}
                    onToggle={() => {
                      if (expandedId === action.id) {
                        setExpandedId(null)
                        setResponse('')
                      } else {
                        setExpandedId(action.id)
                        setResponse('')
                      }
                    }}
                    onResponseChange={setResponse}
                    onComplete={() => completeMutation.mutate({ id: action.id, responseJson: response || null })}
                    onReject={() => rejectMutation.mutate({ id: action.id, responseJson: response || null })}
                  />
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      <PaginationControls
        page={actionsQuery.data?.page ?? search.page}
        pageSize={actionsQuery.data?.pageSize ?? search.pageSize}
        totalCount={actionsQuery.data?.totalCount ?? 0}
        totalPages={actionsQuery.data?.totalPages ?? 0}
        onPageChange={(page) => void navigate({ search: (prev) => ({ ...prev, page }) })}
        onPageSizeChange={(pageSize) => void navigate({ search: (prev) => ({ ...prev, pageSize, page: 1 }) })}
      />
    </section>
  )
}

function ActionRow({
  action,
  isExpanded,
  response,
  isSubmitting,
  onToggle,
  onResponseChange,
  onComplete,
  onReject,
}: {
  action: WorkflowActionItem
  isExpanded: boolean
  response: string
  isSubmitting: boolean
  onToggle: () => void
  onResponseChange: (value: string) => void
  onComplete: () => void
  onReject: () => void
}) {
  const isPending = action.status === 'Pending'

  return (
    <>
      <TableRow className={`cursor-pointer ${isExpanded ? 'bg-primary/5' : ''}`} onClick={onToggle}>
        <TableCell className="font-medium">
          {action.workflowName ?? '—'}
        </TableCell>
        <TableCell>
          <Badge variant="outline" className={`rounded-full ${actionTypeBadgeColor(action.actionType)}`}>
            {action.actionType}
          </Badge>
        </TableCell>
        <TableCell className="max-w-[250px] truncate text-sm text-muted-foreground">
          {action.instructions ?? 'No instructions provided'}
        </TableCell>
        <TableCell>
          <Badge variant={actionStatusVariant(action.status)}>{action.status}</Badge>
        </TableCell>
        <TableCell className="text-sm text-muted-foreground">
          {action.dueAt ? new Date(action.dueAt).toLocaleString() : '—'}
        </TableCell>
        <TableCell className="text-sm text-muted-foreground">
          {new Date(action.createdAt).toLocaleString()}
        </TableCell>
      </TableRow>
      {isExpanded && (
        <TableRow>
          <TableCell colSpan={6} className="bg-muted/20 p-4">
            <div className="space-y-4">
              {action.instructions && (
                <div className="space-y-1">
                  <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">Instructions</p>
                  <p className="text-sm">{action.instructions}</p>
                </div>
              )}
              {action.contextJson && (
                <div className="space-y-1">
                  <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">Context</p>
                  <pre className="max-h-32 overflow-auto rounded-lg border border-border/60 bg-background/50 p-2 text-[11px]">
                    {(() => {
                      try {
                        return JSON.stringify(JSON.parse(action.contextJson), null, 2)
                      } catch {
                        return action.contextJson
                      }
                    })()}
                  </pre>
                </div>
              )}
              {isPending && (
                <div className="space-y-3">
                  <div className="space-y-1">
                    <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">Response (optional)</p>
                    <Textarea
                      value={response}
                      onChange={(e) => onResponseChange(e.target.value)}
                      placeholder="Add any notes or response..."
                      rows={3}
                      className="rounded-lg"
                    />
                  </div>
                  <div className="flex items-center gap-2">
                    <Button size="sm" onClick={onComplete} disabled={isSubmitting}>
                      <Check className="mr-1 size-3.5" /> Complete
                    </Button>
                    <Button variant="outline" size="sm" className="text-destructive" onClick={onReject} disabled={isSubmitting}>
                      <XCircle className="mr-1 size-3.5" /> Reject
                    </Button>
                  </div>
                </div>
              )}
              {action.responseJson && (
                <div className="space-y-1">
                  <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">Response</p>
                  <p className="text-sm">{action.responseJson}</p>
                </div>
              )}
            </div>
          </TableCell>
        </TableRow>
      )}
    </>
  )
}
