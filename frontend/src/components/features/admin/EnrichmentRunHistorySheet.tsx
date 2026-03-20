import { useEffect, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { fetchEnrichmentRuns, type EnrichmentRun } from '@/server/system.functions'
import { Badge } from '@/components/ui/badge'
import { PaginationControls } from '@/components/ui/pagination-controls'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'
import { cn } from '@/lib/utils'

type EnrichmentRunHistorySheetProps = {
  sourceKey: string | null
  sourceDisplayName: string | null
  isOpen: boolean
  onOpenChange: (open: boolean) => void
}

export function EnrichmentRunHistorySheet({
  sourceKey,
  sourceDisplayName,
  isOpen,
  onOpenChange,
}: EnrichmentRunHistorySheetProps) {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const runsMutation = useMutation({
    mutationFn: async (input: { sourceKey: string; page: number; pageSize: number }) =>
      fetchEnrichmentRuns({ data: input }),
  })

  useEffect(() => {
    if (!isOpen || !sourceKey) {
      return
    }

    void runsMutation.mutateAsync({ sourceKey, page, pageSize })
  }, [isOpen, page, pageSize, sourceKey])

  useEffect(() => {
    if (!isOpen) {
      setPage(1)
      setPageSize(10)
    }
  }, [isOpen])

  const data = runsMutation.data
  const runs = data?.items ?? []
  const succeededRuns = runs.filter((run) => run.status.toLowerCase() === 'succeeded').length
  const failedRuns = runs.filter((run) => run.status.toLowerCase() === 'failed').length
  const runningRuns = runs.filter((run) => run.status.toLowerCase() === 'running').length
  const successRate = runs.length ? Math.round((succeededRuns / runs.length) * 100) : 0

  return (
    <Sheet open={isOpen} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full overflow-y-auto border-l border-border/80 bg-background/80 p-0 sm:max-w-3xl">
        <SheetHeader className="border-b border-border/70 bg-muted/20">
          <SheetTitle>{sourceDisplayName ?? 'Enrichment history'}</SheetTitle>
          <SheetDescription>
            Recent enrichment runs with job outcome counters for this provider.
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

          {!data && runsMutation.isPending ? (
            <p className="text-sm text-muted-foreground">Loading enrichment history...</p>
          ) : null}

          {data?.items.length ? (
            <div className="space-y-3">
              {data.items.map((run) => (
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

          {data && data.items.length === 0 ? (
            <div className="rounded-2xl border border-dashed border-border/70 bg-background/30 px-4 py-8 text-sm text-muted-foreground">
              No enrichment runs have been recorded for this source yet.
            </div>
          ) : null}
        </div>
      </SheetContent>
    </Sheet>
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
        'rounded-2xl border px-4 py-3',
        tone === 'success' && 'border-tone-success-border bg-tone-success',
        tone === 'warning' && 'border-tone-warning-border bg-tone-warning',
        tone === 'error' && 'border-destructive/25 bg-destructive/10',
        tone === 'neutral' && 'border-border/70 bg-background/30',
      )}
    >
      <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">{label}</p>
      <p className="mt-2 text-2xl font-semibold text-foreground">{value}</p>
    </div>
  )
}

function RunHistoryCard({ run }: { run: EnrichmentRun }) {
  return (
    <div className="rounded-3xl border border-border/70 bg-card/85 p-4 shadow-sm">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <Badge
            variant="outline"
            className={cn(
              'rounded-full',
              run.status.toLowerCase() === 'succeeded' && 'border-tone-success-border bg-tone-success text-tone-success-foreground',
              run.status.toLowerCase() === 'running' && 'border-tone-warning-border bg-tone-warning text-tone-warning-foreground',
              run.status.toLowerCase() === 'failed' && 'border-destructive/25 bg-destructive/10 text-destructive',
              run.status.toLowerCase() === 'nowork' && 'border-border/70 bg-background/70 text-muted-foreground',
            )}
          >
            {run.status}
          </Badge>
          <span className="text-muted-foreground">Started {formatTimestamp(run.startedAt)}</span>
          <span className="text-muted-foreground">Completed {formatTimestamp(run.completedAt)}</span>
        </div>
        <span className="text-xs text-muted-foreground">{run.id}</span>
      </div>

      <div className="mt-4 grid gap-2 text-xs text-muted-foreground sm:grid-cols-3 lg:grid-cols-5">
        <RunMetric label="Claimed" value={run.jobsClaimed} />
        <RunMetric label="Succeeded" value={run.jobsSucceeded} />
        <RunMetric label="No Data" value={run.jobsNoData} />
        <RunMetric label="Failed" value={run.jobsFailed} />
        <RunMetric label="Retried" value={run.jobsRetried} />
      </div>

      {run.lastError ? <p className="mt-3 text-xs text-destructive">Error: {run.lastError}</p> : null}
    </div>
  )
}

function RunMetric({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-xl border border-border/60 bg-background/30 px-3 py-2">
      <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">{label}</p>
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
