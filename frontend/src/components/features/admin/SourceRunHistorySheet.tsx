import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchTenantIngestionRuns } from '@/api/settings.functions'
import type { TenantIngestionRun } from '@/api/settings.schemas'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { PaginationControls } from '@/components/ui/pagination-controls'
import { DataTableActiveFilters } from '@/components/ui/data-table-workbench'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'
import { cn } from '@/lib/utils'

type SourceRunHistorySheetProps = {
  tenantId: string
  sourceKey: string | null
  sourceDisplayName: string | null
  isOpen: boolean
  onOpenChange: (open: boolean) => void
}

type RunFilter = 'all' | 'active' | 'failed-recoverable' | 'failed-terminal' | 'succeeded'
type RunActiveFilter = {
  key: string
  label: string
  onClear: () => void
}

export function SourceRunHistorySheet({
  tenantId,
  sourceKey,
  sourceDisplayName,
  isOpen,
  onOpenChange,
}: SourceRunHistorySheetProps) {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [filter, setFilter] = useState<RunFilter>('all')
  const runsQuery = useQuery({
    queryKey: ['tenant-ingestion-runs', tenantId, sourceKey, page, pageSize],
    enabled: isOpen && Boolean(sourceKey),
    queryFn: async () =>
      fetchTenantIngestionRuns({
        data: {
          tenantId,
          sourceKey: sourceKey!,
          page,
          pageSize,
        },
      }),
  })

  const data = runsQuery.data
  const runs = data?.items ?? []
  const filteredRuns = runs.filter((run) => matchesRunFilter(run, filter))
  const succeededRuns = runs.filter((run) => getRunTone(run.status) === 'success').length
  const failedRuns = runs.filter((run) => getRunTone(run.status) === 'error').length
  const runningRuns = runs.filter((run) => getRunTone(run.status) === 'warning').length
  const recoverableFailedRuns = runs.filter((run) => getRunCategory(run.status) === 'failed-recoverable').length
  const terminalFailedRuns = runs.filter((run) => getRunCategory(run.status) === 'failed-terminal').length
  const successRate = runs.length ? Math.round((succeededRuns / runs.length) * 100) : 0
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

  function handleOpenChange(open: boolean) {
    if (!open) {
      setPage(1)
      setPageSize(10)
      setFilter('all')
    }

    onOpenChange(open)
  }

  return (
    <Sheet open={isOpen} onOpenChange={handleOpenChange}>
      <SheetContent side="right" className="w-full overflow-y-auto border-l border-border/80 bg-background/98 p-0 sm:max-w-3xl">
        <SheetHeader className="border-b border-border/70 bg-muted/20">
          <SheetTitle>{sourceDisplayName ?? 'Source history'}</SheetTitle>
          <SheetDescription>
            Recent ingestion runs with staged, merged, and reconciliation counters for this source.
          </SheetDescription>
        </SheetHeader>

        <div className="space-y-4 p-6">
          {data ? (
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
              <SummaryCard
                label="Page Success Rate"
                value={`${successRate}%`}
                tone={successRate >= 80 ? 'success' : successRate >= 50 ? 'warning' : 'error'}
              />
              <SummaryCard label="Succeeded" value={String(succeededRuns)} tone="success" />
              <SummaryCard label="Failed" value={String(failedRuns)} tone={failedRuns > 0 ? 'error' : 'neutral'} />
              <SummaryCard label="Running" value={String(runningRuns)} tone={runningRuns > 0 ? 'warning' : 'neutral'} />
            </div>
          ) : null}

          {!data && runsQuery.isPending ? (
            <p className="text-sm text-muted-foreground">Loading run history...</p>
          ) : null}

          {data?.items.length ? (
            <div className="space-y-3">
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
            </div>
          ) : null}

          {data?.items.length ? (
            <div className="space-y-3">
              {filteredRuns.map((run) => (
                <RunHistoryCard key={run.id} run={run} />
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
            <div className="rounded-2xl border border-dashed border-border/70 bg-background/20 px-4 py-8 text-sm text-muted-foreground">
              {runs.length === 0
                ? 'No ingestion runs have been recorded for this source yet.'
                : 'No runs on this page match the selected status filter.'}
            </div>
          ) : null}
        </div>
      </SheetContent>
    </Sheet>
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
        tone === 'success' && 'border-emerald-400/25 bg-emerald-400/10',
        tone === 'warning' && 'border-amber-400/25 bg-amber-400/10',
        tone === 'error' && 'border-destructive/25 bg-destructive/10',
        tone === 'neutral' && 'border-border/70 bg-background/35',
      )}
    >
      <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">{label}</p>
      <p className="mt-2 text-2xl font-semibold text-foreground">{value}</p>
    </div>
  )
}

function RunHistoryCard({ run }: { run: TenantIngestionRun }) {
  const tone = getRunTone(run.status)

  return (
    <div className="rounded-[26px] border border-border/70 bg-card/82 p-4 shadow-sm">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <Badge
            variant="outline"
            className={cn(
              'rounded-full',
              tone === 'success' && 'border-emerald-400/25 bg-emerald-400/10 text-emerald-300',
              tone === 'warning' && 'border-amber-400/25 bg-amber-400/10 text-amber-300',
              tone === 'error' && 'border-destructive/25 bg-destructive/10 text-destructive',
            )}
          >
            {run.status}
          </Badge>
          <span className="text-muted-foreground">Started {formatTimestamp(run.startedAt)}</span>
          <span className="text-muted-foreground">Completed {formatTimestamp(run.completedAt)}</span>
        </div>
        <span className="text-xs text-muted-foreground">{run.id}</span>
      </div>

      <div className="mt-4 grid gap-3 sm:grid-cols-3">
        <RunSummaryMetric label="Latest phase" value={formatPhase(run.latestPhase)} />
        <RunSummaryMetric
          label="Latest batch"
          value={run.latestBatchNumber !== null ? String(run.latestBatchNumber) : '—'}
        />
        <RunSummaryMetric
          label="Checkpoint"
          value={
            run.latestCheckpointStatus
              ? `${run.latestCheckpointStatus} · ${run.latestRecordsCommitted ?? 0} rows`
              : '—'
          }
        />
      </div>

      <div className="mt-4 grid gap-2 text-xs text-muted-foreground sm:grid-cols-3 lg:grid-cols-4">
        <RunMetric label="Fetched Vulns" value={run.fetchedVulnerabilityCount} />
        <RunMetric label="Fetched Assets" value={run.fetchedAssetCount} />
        <RunMetric label="Fetched SW Links" value={run.fetchedSoftwareInstallationCount} />
        <RunMetric label="Staged Vulns" value={run.stagedVulnerabilityCount} />
        <RunMetric label="Staged Exposures" value={run.stagedExposureCount} />
        <RunMetric label="Merged Exposures" value={run.mergedExposureCount} />
        <RunMetric label="Opened Projections" value={run.openedProjectionCount} />
        <RunMetric label="Resolved Projections" value={run.resolvedProjectionCount} />
        <RunMetric label="Staged Assets" value={run.stagedAssetCount} />
        <RunMetric label="Merged Assets" value={run.mergedAssetCount} />
        <RunMetric label="Staged SW Links" value={run.stagedSoftwareLinkCount} />
        <RunMetric label="Resolved SW Links" value={run.resolvedSoftwareLinkCount} />
        <RunMetric label="Installs Created" value={run.installationsCreated} />
        <RunMetric label="Installs Touched" value={run.installationsTouched} />
        <RunMetric label="Episodes Opened" value={run.installationEpisodesOpened} />
        <RunMetric label="Episodes Seen" value={run.installationEpisodesSeen} />
        <RunMetric label="Stale Installs" value={run.staleInstallationsMarked} />
        <RunMetric label="Installs Removed" value={run.installationsRemoved} />
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

function RunSummaryMetric({ label, value }: { label: string; value: string }) {
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

function formatPhase(value: string | null) {
  if (!value) {
    return '—'
  }

  return value
    .split('-')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ')
}
