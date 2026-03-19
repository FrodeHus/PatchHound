import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useRouter } from '@tanstack/react-router'
import { RotateCw, Square, Trash2 } from 'lucide-react'
import {
  abortTenantIngestionRun,
  deleteTenantIngestionRun,
  fetchTenantIngestionRuns,
  triggerTenantIngestionSync,
} from '@/api/settings.functions'
import type { TenantIngestionRun } from '@/api/settings.schemas'
import { InsetPanel } from '@/components/ui/inset-panel'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { PaginationControls } from '@/components/ui/pagination-controls'
import { DataTableActiveFilters } from '@/components/ui/data-table-workbench'
import { useSSE } from '@/hooks/useSSE'
import { cn } from '@/lib/utils'

type SourceRunHistoryViewProps = {
  tenantId: string
  sourceKey: string
  sourceDisplayName: string
}

type RunFilter = 'all' | 'active' | 'failed-recoverable' | 'failed-terminal' | 'succeeded'
type RunActiveFilter = {
  key: string
  label: string
  onClear: () => void
}

export function SourceRunHistoryView({
  tenantId,
  sourceKey,
  sourceDisplayName,
}: SourceRunHistoryViewProps) {
  const router = useRouter()
  const queryClient = useQueryClient()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [filter, setFilter] = useState<RunFilter>('all')
  const [runPendingAbort, setRunPendingAbort] = useState<TenantIngestionRun | null>(null)
  const [runPendingDelete, setRunPendingDelete] = useState<TenantIngestionRun | null>(null)
  const runsQuery = useQuery({
    queryKey: ['tenant-ingestion-runs', tenantId, sourceKey, page, pageSize],
    enabled: true,
    queryFn: async () =>
      fetchTenantIngestionRuns({
        data: {
          tenantId,
          sourceKey,
          page,
          pageSize,
        },
      }),
  })
  const resumeMutation = useMutation({
    mutationFn: async () => {
      await triggerTenantIngestionSync({
        data: {
          tenantId,
          sourceKey,
        },
      })
    },
    onSuccess: async () => {
      await router.invalidate()
    },
  })
  const deleteMutation = useMutation({
    mutationFn: async (runId: string) => {
      await deleteTenantIngestionRun({
        data: {
          tenantId,
          sourceKey,
          runId,
        },
      })
    },
    onSuccess: async () => {
      setRunPendingDelete(null)
      await router.invalidate()
    },
  })
  const abortMutation = useMutation({
    mutationFn: async (runId: string) => {
      await abortTenantIngestionRun({
        data: {
          tenantId,
          sourceKey,
          runId,
        },
      })
    },
    onSuccess: async () => {
      setRunPendingAbort(null)
      await router.invalidate()
    },
  })

  const data = runsQuery.data
  const runs = data?.items ?? []
  const activeRun = runs.find((run) => getRunCategory(run.status) === 'active') ?? null

  useSSE(
    'IngestionRunProgress',
    (payload) => {
      if (!payload || typeof payload !== 'object' || !('id' in payload)) {
        return
      }

      const progress = payload as TenantIngestionRun
      queryClient.setQueryData(
        ['tenant-ingestion-runs', tenantId, sourceKey, page, pageSize],
        (current: typeof data | undefined) => {
          if (!current) {
            return current
          }

          return {
            ...current,
            items: current.items.map((item) => (item.id === progress.id ? { ...item, ...progress } : item)),
          }
        },
      )
    },
    {
      enabled: Boolean(activeRun),
      url:
        activeRun
          ? `/api/ingestion-run-events?tenantId=${encodeURIComponent(tenantId)}&sourceKey=${encodeURIComponent(sourceKey)}&runId=${encodeURIComponent(activeRun.id)}`
          : undefined,
    },
  )

  const filteredRuns = runs.filter((run) => matchesRunFilter(run, filter))
  const succeededRuns = runs.filter((run) => getRunTone(run.status) === 'success').length
  const failedRuns = runs.filter((run) => getRunTone(run.status) === 'error').length
  const runningRuns = runs.filter((run) => getRunTone(run.status) === 'warning').length
  const recoverableFailedRuns = runs.filter((run) => getRunCategory(run.status) === 'failed-recoverable').length
  const terminalFailedRuns = runs.filter((run) => getRunCategory(run.status) === 'failed-terminal').length
  const activeFilters: RunActiveFilter[] =
    filter === 'all'
      ? []
      : [
          {
            key: 'status',
            label: `Status: ${getFilterLabel(filter)}`,
            onClear: () => setFilter('all'),
          },
        ]

  return (
    <div className="space-y-5">
      <div className="space-y-2">
        <h2 className="text-2xl font-semibold tracking-tight">{sourceDisplayName} run history</h2>
        <p className="max-w-3xl text-sm leading-6 text-muted-foreground">
          Review batch progress, merge state, and recent recoverable or terminal failures for this ingestion source.
        </p>
      </div>

      {data ? (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <SummaryCard label="Succeeded" value={String(succeededRuns)} tone="success" />
          <SummaryCard label="Failed" value={String(failedRuns)} tone={failedRuns > 0 ? 'error' : 'neutral'} />
          <SummaryCard label="Active" value={String(runningRuns)} tone={runningRuns > 0 ? 'warning' : 'neutral'} />
          <SummaryCard
            label="Recoverable Failed"
            value={String(recoverableFailedRuns)}
            tone={recoverableFailedRuns > 0 ? 'warning' : 'neutral'}
          />
        </div>
      ) : null}

      {!data && runsQuery.isPending ? (
        <InsetPanel emphasis="subtle" className="px-4 py-8 text-sm text-muted-foreground">
          Loading run history...
        </InsetPanel>
      ) : null}

      {data?.items.length ? (
        <InsetPanel className="space-y-4 p-4">
          <div className="flex flex-wrap items-center gap-2">
            <RunFilterButton active={filter === 'all'} count={runs.length} onClick={() => setFilter('all')}>
              All
            </RunFilterButton>
            <RunFilterButton active={filter === 'active'} count={runningRuns} onClick={() => setFilter('active')}>
              Active
            </RunFilterButton>
            <RunFilterButton
              active={filter === 'failed-recoverable'}
              count={recoverableFailedRuns}
              onClick={() => setFilter('failed-recoverable')}
            >
              Recoverable failed
            </RunFilterButton>
            <RunFilterButton
              active={filter === 'failed-terminal'}
              count={terminalFailedRuns}
              onClick={() => setFilter('failed-terminal')}
            >
              Terminal failed
            </RunFilterButton>
            <RunFilterButton
              active={filter === 'succeeded'}
              count={succeededRuns}
              onClick={() => setFilter('succeeded')}
            >
              Succeeded
            </RunFilterButton>
          </div>

          <DataTableActiveFilters filters={activeFilters} onClearAll={() => setFilter('all')} />
        </InsetPanel>
      ) : null}

      {data?.items.length ? (
          <div className="space-y-3">
          {filteredRuns.map((run) => (
            <RunHistoryCard
              key={run.id}
              run={run}
              canResume={getRunCategory(run.status) === 'failed-recoverable'}
              canAbort={getRunCategory(run.status) === 'active'}
              canDelete={getRunCategory(run.status) !== 'active'}
              isResuming={resumeMutation.isPending}
              isAborting={abortMutation.isPending && runPendingAbort?.id === run.id}
              isDeleting={deleteMutation.isPending && runPendingDelete?.id === run.id}
              onResume={() => resumeMutation.mutate()}
              onAbort={() => setRunPendingAbort(run)}
              onDelete={() => setRunPendingDelete(run)}
            />
          ))}
            <PaginationControls
              page={data.page}
            pageSize={data.pageSize}
            totalCount={data.totalCount}
            totalPages={data.totalPages}
            onPageChange={setPage}
            onPageSizeChange={(value) => {
              setPageSize(value)
              setPage(1)
            }}
          />
        </div>
      ) : null}

      {data && filteredRuns.length === 0 ? (
        <InsetPanel emphasis="subtle" className="border-dashed px-4 py-8 text-sm text-muted-foreground">
          {runs.length === 0
            ? 'No ingestion runs have been recorded for this source yet.'
            : 'No runs on this page match the selected status filter.'}
        </InsetPanel>
      ) : null}

      <Dialog open={runPendingDelete !== null} onOpenChange={(open) => {
        if (!open && !deleteMutation.isPending) {
          setRunPendingDelete(null)
        }
      }}>
        <DialogContent className="w-full max-w-lg rounded-2xl border-border/80 bg-card p-0 sm:max-w-lg">
          <DialogHeader className="border-b border-border/60 px-6 py-5">
            <DialogTitle>Delete ingestion run</DialogTitle>
            <DialogDescription>
              This removes the selected run and all staged data associated with it. Use this to clean out a faulty
              ingestion before starting again.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-3 px-6 py-5 text-sm text-muted-foreground">
            <p>
              <span className="font-medium text-foreground">Run:</span>{' '}
              {runPendingDelete ? `${runPendingDelete.status} · ${formatTimestamp(runPendingDelete.startedAt)}` : '—'}
            </p>
            <p>
              Staged machines: {runPendingDelete?.stagedMachineCount ?? 0} · staged vulnerabilities:{' '}
              {runPendingDelete?.stagedVulnerabilityCount ?? 0} · staged software:{' '}
              {runPendingDelete?.stagedSoftwareCount ?? 0}
            </p>
          </div>
          <DialogFooter className="border-t border-border/60 bg-card px-6 py-4">
            <Button
              type="button"
              variant="outline"
              disabled={deleteMutation.isPending}
              onClick={() => setRunPendingDelete(null)}
            >
              Cancel
            </Button>
            <Button
              type="button"
              variant="destructive"
              disabled={!runPendingDelete || deleteMutation.isPending}
              onClick={() => {
                if (runPendingDelete) {
                  deleteMutation.mutate(runPendingDelete.id)
                }
              }}
            >
              {deleteMutation.isPending ? 'Deleting...' : 'Delete run'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={runPendingAbort !== null} onOpenChange={(open) => {
        if (!open && !abortMutation.isPending) {
          setRunPendingAbort(null)
        }
      }}>
        <DialogContent className="w-full max-w-lg rounded-2xl border-border/80 bg-card p-0 sm:max-w-lg">
          <DialogHeader className="border-b border-border/60 px-6 py-5">
            <DialogTitle>Abort ingestion run</DialogTitle>
            <DialogDescription>
              This asks the worker to stop the active run after the current committed step. Staged data is retained so the run can be resumed later if it fails recoverably.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-3 px-6 py-5 text-sm text-muted-foreground">
            <p>
              <span className="font-medium text-foreground">Run:</span>{' '}
              {runPendingAbort ? `${runPendingAbort.status} · ${formatTimestamp(runPendingAbort.startedAt)}` : '—'}
            </p>
            <p>
              Current phase: {runPendingAbort?.latestPhase ? formatPhase(runPendingAbort.latestPhase) : 'Unknown'}
            </p>
          </div>
          <DialogFooter className="border-t border-border/60 bg-card px-6 py-4">
            <Button
              type="button"
              variant="outline"
              disabled={abortMutation.isPending}
              onClick={() => setRunPendingAbort(null)}
            >
              Cancel
            </Button>
            <Button
              type="button"
              variant="destructive"
              disabled={!runPendingAbort || abortMutation.isPending}
              onClick={() => {
                if (runPendingAbort) {
                  abortMutation.mutate(runPendingAbort.id)
                }
              }}
            >
              {abortMutation.isPending ? 'Aborting...' : 'Abort ingestion'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}

function RunFilterButton({
  active,
  count,
  onClick,
  children,
}: {
  active: boolean
  count: number
  onClick: () => void
  children: string
}) {
  return (
    <Button type="button" size="sm" variant={active ? 'default' : 'outline'} className="rounded-full" onClick={onClick}>
      {children}
      <span className={cn('ml-1 rounded-full px-1.5 text-[11px]', active ? 'bg-primary-foreground/15 text-primary-foreground' : 'bg-muted text-muted-foreground')}>
        {count}
      </span>
    </Button>
  )
}

function SummaryCard({
  label,
  value,
  tone,
}: {
  label: string
  value: string
  tone: 'neutral' | 'success' | 'warning' | 'error'
}) {
  return (
    <div
      className={cn(
        'rounded-[22px] border px-4 py-3',
        tone === 'success' && 'border-tone-success-border bg-tone-success',
        tone === 'warning' && 'border-tone-warning-border bg-tone-warning',
        tone === 'error' && 'border-destructive/25 bg-destructive/10',
        tone === 'neutral' && 'border-border/70 bg-background/35',
      )}
    >
      <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">{label}</p>
      <p className="mt-2 text-2xl font-semibold text-foreground">{value}</p>
    </div>
  )
}

function RunHistoryCard({
  run,
  canResume,
  canAbort,
  canDelete,
  isResuming,
  isAborting,
  isDeleting,
  onResume,
  onAbort,
  onDelete,
}: {
  run: TenantIngestionRun
  canResume: boolean
  canAbort: boolean
  canDelete: boolean
  isResuming: boolean
  isAborting: boolean
  isDeleting: boolean
  onResume: () => void
  onAbort: () => void
  onDelete: () => void
}) {
  const tone = getRunTone(run.status)

  return (
    <div className="rounded-[26px] border border-border/70 bg-card/82 p-4 shadow-sm">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <Badge
            variant="outline"
            className={cn(
              'rounded-full',
              tone === 'success' && 'border-tone-success-border bg-tone-success text-tone-success-foreground',
              tone === 'warning' && 'border-tone-warning-border bg-tone-warning text-tone-warning-foreground',
              tone === 'error' && 'border-destructive/25 bg-destructive/10 text-destructive',
            )}
          >
            {run.status}
          </Badge>
          {run.latestPhase ? (
            <Badge variant="outline" className="rounded-full border-border/70 bg-muted/60 text-muted-foreground">
              {formatPhase(run.latestPhase)}
            </Badge>
          ) : null}
          {run.snapshotStatus ? (
            <Badge variant="outline" className="rounded-full border-border/70 bg-muted/60 text-muted-foreground">
              {formatSnapshotStatus(run.snapshotStatus)}
            </Badge>
          ) : null}
          <span className="text-muted-foreground">Started {formatTimestamp(run.startedAt)}</span>
          <span className="text-muted-foreground">Completed {formatTimestamp(run.completedAt)}</span>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {canResume ? (
            <Button type="button" size="sm" variant="outline" disabled={isResuming} onClick={onResume}>
              <RotateCw className="size-4" />
              {isResuming ? 'Resuming...' : 'Resume ingestion'}
            </Button>
          ) : null}
          {canAbort ? (
            <Button type="button" size="sm" variant="outline" disabled={isAborting} onClick={onAbort}>
              <Square className="size-4" />
              {isAborting ? 'Aborting...' : 'Abort ingestion'}
            </Button>
          ) : null}
          {canDelete ? (
            <Button type="button" size="sm" variant="outline" disabled={isDeleting} onClick={onDelete}>
              <Trash2 className="size-4" />
              {isDeleting ? 'Deleting...' : 'Delete run'}
            </Button>
          ) : null}
          <span className="text-xs text-muted-foreground">{run.id}</span>
        </div>
      </div>

      <div className="mt-4 grid gap-2 text-xs text-muted-foreground sm:grid-cols-2 lg:grid-cols-3">
        <RunMetric label="Staged machines" value={run.stagedMachineCount} />
        <RunMetric label="Staged vulnerabilities" value={run.stagedVulnerabilityCount} />
        <RunMetric label="Staged software" value={run.stagedSoftwareCount} />
        <RunMetric label="Persisted machines" value={run.persistedMachineCount} />
        <RunMetric label="Persisted vulnerabilities" value={run.persistedVulnerabilityCount} />
        <RunMetric label="Persisted software" value={run.persistedSoftwareCount} />
      </div>

      {run.error ? <p className="mt-3 text-xs text-destructive">Error: {run.error}</p> : null}
    </div>
  )
}

function getRunTone(status: string): 'neutral' | 'success' | 'warning' | 'error' {
  const normalizedStatus = getRunCategory(status)

  if (normalizedStatus === 'succeeded') {
    return 'success'
  }

  if (normalizedStatus === 'failed-recoverable' || normalizedStatus === 'failed-terminal') {
    return 'error'
  }

  if (normalizedStatus === 'active') {
    return 'warning'
  }

  return 'neutral'
}

function getRunCategory(status: string): RunFilter | 'neutral' {
  const normalizedStatus = status.trim().toLowerCase()

  if (normalizedStatus === 'succeeded') {
    return 'succeeded'
  }

  if (normalizedStatus === 'failedrecoverable') {
    return 'failed-recoverable'
  }

  if (normalizedStatus === 'failedterminal') {
    return 'failed-terminal'
  }

  if (normalizedStatus === 'failed') {
    return 'failed-recoverable'
  }

  if (normalizedStatus === 'staging' || normalizedStatus === 'mergepending' || normalizedStatus === 'merging') {
    return 'active'
  }

  return 'neutral'
}

function matchesRunFilter(run: TenantIngestionRun, filter: RunFilter) {
  if (filter === 'all') {
    return true
  }

  return getRunCategory(run.status) === filter
}

function getFilterLabel(filter: Exclude<RunFilter, 'all'>) {
  switch (filter) {
    case 'active':
      return 'Active'
    case 'failed-recoverable':
      return 'Recoverable failed'
    case 'failed-terminal':
      return 'Terminal failed'
    case 'succeeded':
      return 'Succeeded'
  }
}

function RunMetric({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-xl border border-border/60 bg-background/35 px-3 py-2">
      <p className="text-[11px] uppercase tracking-[0.12em] text-muted-foreground">{label}</p>
      <p className="mt-1 text-sm font-medium text-foreground">{value}</p>
    </div>
  )
}

function formatTimestamp(value: string | null) {
  if (!value) {
    return 'Never'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return new Intl.DateTimeFormat('en', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}

function formatPhase(value: string) {
  return value
    .split('-')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ')
}

function formatSnapshotStatus(value: string) {
  return value
    .split(/(?=[A-Z])|-/)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ')
}
