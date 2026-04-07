import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { Eye, Play } from 'lucide-react'
import { toast } from 'sonner'
import { useMutation } from '@tanstack/react-query'
import { fetchScanRuns, triggerScanRun } from '@/api/authenticated-scans.functions'
import type { PagedScanRuns, ScanRun } from '@/api/authenticated-scans.schemas'
import { formatDateTime } from '@/lib/formatting'
import { ScanRunReportDialog } from './ScanRunReportDialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import { PaginationControls } from '@/components/ui/pagination-controls'

type Props = {
  initialData: PagedScanRuns
  page: number
  pageSize: number
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
}

export function ScanRunHistoryTab({ initialData, page, pageSize, onPageChange, onPageSizeChange }: Props) {
  const [reportRunId, setReportRunId] = useState<string | null>(null)

  const query = useQuery({
    queryKey: ['scan-runs', page, pageSize],
    queryFn: () => fetchScanRuns({ data: { page, pageSize } }),
    initialData,
  })

  const triggerMutation = useMutation({
    mutationFn: triggerScanRun,
    onSuccess: () => toast.success('Scan run triggered'),
    onError: () => toast.error('Failed to trigger scan run'),
  })

  const columns: ColumnDef<ScanRun>[] = [
    {
      accessorKey: 'profileName',
      header: 'Profile',
    },
    {
      accessorKey: 'triggerKind',
      header: 'Trigger',
      cell: ({ row }) => (
        <Badge variant="outline" className="text-[10px]">
          {row.original.triggerKind}
        </Badge>
      ),
    },
    {
      accessorKey: 'startedAt',
      header: 'Started',
      cell: ({ row }) => formatDateTime(row.original.startedAt),
    },
    {
      accessorKey: 'completedAt',
      header: 'Completed',
      cell: ({ row }) =>
        row.original.completedAt ? formatDateTime(row.original.completedAt) : (
          <span className="text-muted-foreground">Running...</span>
        ),
    },
    {
      accessorKey: 'status',
      header: 'Status',
      cell: ({ row }) => <StatusBadge status={row.original.status} />,
    },
    {
      id: 'devices',
      header: 'Devices',
      cell: ({ row }) => (
        <span className="tabular-nums">
          {row.original.succeededCount}/{row.original.totalDevices}
        </span>
      ),
    },
    {
      accessorKey: 'entriesIngested',
      header: 'Entries',
      cell: ({ row }) => (
        <span className="tabular-nums">{row.original.entriesIngested}</span>
      ),
    },
    {
      id: 'actions',
      cell: ({ row }) => (
        <div className="flex gap-1">
          <Button
            size="sm"
            variant="ghost"
            title="Re-trigger this profile"
            onClick={() =>
              triggerMutation.mutate({ data: { id: row.original.scanProfileId } })
            }
            disabled={triggerMutation.isPending}
          >
            <Play className="h-3 w-3" />
          </Button>
          <Button
            size="sm"
            variant="ghost"
            title="View report"
            onClick={() => setReportRunId(row.original.id)}
          >
            <Eye className="h-3 w-3" />
          </Button>
        </div>
      ),
    },
  ]

  return (
    <>
      <Card>
        <CardHeader>
          <CardTitle>Scan Run History</CardTitle>
        </CardHeader>
        <CardContent>
          <DataTable columns={columns} data={query.data?.items ?? []} />
          <PaginationControls
            page={page}
            pageSize={pageSize}
            totalCount={query.data?.totalCount ?? 0}
            totalPages={query.data?.totalPages ?? 0}
            onPageChange={onPageChange}
            onPageSizeChange={onPageSizeChange}
          />
        </CardContent>
      </Card>

      <ScanRunReportDialog
        runId={reportRunId}
        open={Boolean(reportRunId)}
        onOpenChange={(open) => { if (!open) setReportRunId(null) }}
      />
    </>
  )
}

function StatusBadge({ status }: { status: string }) {
  switch (status) {
    case 'Succeeded':
      return <Badge variant="default" className="bg-green-600">Succeeded</Badge>
    case 'Failed':
      return <Badge variant="destructive">Failed</Badge>
    case 'PartiallyFailed':
      return <Badge variant="outline" className="border-amber-500 text-amber-600">Partial</Badge>
    case 'Running':
      return <Badge variant="secondary">Running</Badge>
    case 'Queued':
      return <Badge variant="secondary">Queued</Badge>
    default:
      return <Badge variant="secondary">{status}</Badge>
  }
}
