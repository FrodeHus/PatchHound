import { useQuery } from '@tanstack/react-query'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { Fragment, useState } from 'react'
import { fetchScanRunDetail } from '@/api/authenticated-scans.functions'
import type { ScanJobSummary, ScanRunDetail } from '@/api/authenticated-scans.schemas'
import { formatDateTime } from '@/lib/formatting'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'

type Props = {
  runId: string | null
  open: boolean
  onOpenChange: (open: boolean) => void
}

export function ScanRunReportDialog({ runId, open, onOpenChange }: Props) {
  const query = useQuery({
    queryKey: ['scan-run-detail', runId],
    queryFn: () => fetchScanRunDetail({ data: { id: runId! } }),
    enabled: open && Boolean(runId),
  })

  const detail = query.data

  const succeeded = detail?.jobs.filter((j) => j.status === 'Succeeded') ?? []
  const failed = detail?.jobs.filter((j) => j.status !== 'Succeeded') ?? []

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-3xl max-h-[80vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>
            Scan Run Report
            {detail ? ` — ${detail.profileName}` : ''}
          </DialogTitle>
          <DialogDescription>
            {detail
              ? `${detail.triggerKind} run started ${formatDateTime(detail.startedAt)}`
              : 'Loading...'}
          </DialogDescription>
        </DialogHeader>

        {query.isLoading ? (
          <p className="py-4 text-sm text-muted-foreground">Loading run details...</p>
        ) : query.isError ? (
          <p className="py-4 text-sm text-destructive">Failed to load run details.</p>
        ) : detail ? (
          <div className="space-y-6">
            <RunSummaryBar detail={detail} />

            {succeeded.length > 0 && (
              <SucceededHostsSection jobs={succeeded} />
            )}

            {failed.length > 0 && (
              <FailedHostsSection jobs={failed} />
            )}

            {detail.jobs.length === 0 && (
              <p className="text-sm text-muted-foreground">No jobs were created for this run.</p>
            )}
          </div>
        ) : null}
      </DialogContent>
    </Dialog>
  )
}

function RunSummaryBar({ detail }: { detail: ScanRunDetail }) {
  return (
    <div className="flex flex-wrap gap-3">
      <Badge variant={statusVariant(detail.status)}>{detail.status}</Badge>
      <span className="text-sm text-muted-foreground">
        {detail.totalDevices} devices &middot; {detail.succeededCount} succeeded &middot;{' '}
        {detail.failedCount} failed &middot; {detail.entriesIngested} entries ingested
      </span>
      {detail.completedAt && (
        <span className="text-sm text-muted-foreground">
          Completed {formatDateTime(detail.completedAt)}
        </span>
      )}
    </div>
  )
}

function SucceededHostsSection({ jobs }: { jobs: ScanJobSummary[] }) {
  const [expanded, setExpanded] = useState(false)

  return (
    <div>
      <button
        type="button"
        className="flex items-center gap-1 text-sm font-medium"
        onClick={() => setExpanded(!expanded)}
      >
        {expanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
        Successful hosts ({jobs.length})
      </button>
      {expanded && (
        <Table className="mt-2">
          <TableHeader>
            <TableRow>
              <TableHead>Host</TableHead>
              <TableHead>Entries</TableHead>
              <TableHead>Completed</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {jobs.map((job) => (
              <TableRow key={job.id}>
                <TableCell className="font-mono text-sm">{job.deviceName}</TableCell>
                <TableCell>{job.entriesIngested}</TableCell>
                <TableCell>{job.completedAt ? formatDateTime(job.completedAt) : '—'}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  )
}

function FailedHostsSection({ jobs }: { jobs: ScanJobSummary[] }) {
  return (
    <div>
      <p className="text-sm font-medium text-destructive">Failed hosts ({jobs.length})</p>
      <Table className="mt-2">
        <TableHeader>
          <TableRow>
            <TableHead>Host</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Error</TableHead>
            <TableHead>Attempts</TableHead>
            <TableHead>Duration</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {jobs.map((job) => (
            <Fragment key={job.id}>
              <TableRow>
                <TableCell className="font-mono text-sm">{job.deviceName}</TableCell>
                <TableCell>
                  <Badge variant="destructive" className="text-[10px]">{job.status}</Badge>
                </TableCell>
                <TableCell className="max-w-[200px] truncate text-sm">{job.errorMessage || '—'}</TableCell>
                <TableCell>{job.attemptCount}</TableCell>
                <TableCell>
                  {job.startedAt && job.completedAt
                    ? `${Math.round((new Date(job.completedAt).getTime() - new Date(job.startedAt).getTime()) / 1000)}s`
                    : '—'}
                </TableCell>
              </TableRow>
              {job.validationIssues.length > 0 && (
                <TableRow key={`${job.id}-issues`}>
                  <TableCell colSpan={5} className="bg-muted/30 py-2">
                    <p className="mb-1 text-xs font-medium text-muted-foreground">
                      Validation issues ({job.validationIssues.length})
                    </p>
                    <div className="space-y-0.5">
                      {job.validationIssues.map((issue, i) => (
                        <p key={i} className="font-mono text-xs text-muted-foreground">
                          [{issue.entryIndex}] <span className="text-foreground">{issue.fieldPath}</span>: {issue.message}
                        </p>
                      ))}
                    </div>
                  </TableCell>
                </TableRow>
              )}
            </Fragment>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

function statusVariant(status: string): 'default' | 'destructive' | 'secondary' | 'outline' {
  switch (status) {
    case 'Succeeded':
      return 'default'
    case 'Failed':
      return 'destructive'
    case 'PartiallyFailed':
      return 'outline'
    default:
      return 'secondary'
  }
}
