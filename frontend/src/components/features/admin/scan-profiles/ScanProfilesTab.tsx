import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { toast } from 'sonner'
import { PenSquare, Play, Plus, Trash2 } from 'lucide-react'
import {
  createScanProfile,
  deleteScanProfile,
  fetchScanProfiles,
  triggerScanRun,
  updateScanProfile,
} from '@/api/authenticated-scans.functions'
import type {
  ConnectionProfile,
  PagedScanProfiles,
  ScanProfile,
  ScanRunner,
  ScanningTool,
} from '@/api/authenticated-scans.schemas'
import { ScanProfileDialog } from './ScanProfileDialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import { PaginationControls } from '@/components/ui/pagination-controls'

type Props = {
  initialData: PagedScanProfiles
  runners: ScanRunner[]
  connections: ConnectionProfile[]
  tools: ScanningTool[]
  page: number
  pageSize: number
  onPageChange: (page: number) => void
}

export function ScanProfilesTab({ initialData, runners, connections, tools, page, pageSize, onPageChange }: Props) {
  const queryClient = useQueryClient()
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editing, setEditing] = useState<ScanProfile | null>(null)

  const query = useQuery({
    queryKey: ['scan-profiles', page, pageSize],
    queryFn: () => fetchScanProfiles({ data: { page, pageSize } }),
    initialData,
  })

  const createMutation = useMutation({
    mutationFn: createScanProfile,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scan-profiles'] })
      setDialogOpen(false)
      toast.success('Scan profile created')
    },
    onError: () => toast.error('Failed to create scan profile'),
  })

  const updateMutation = useMutation({
    mutationFn: updateScanProfile,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scan-profiles'] })
      setDialogOpen(false)
      setEditing(null)
      toast.success('Scan profile updated')
    },
    onError: () => toast.error('Failed to update scan profile'),
  })

  const deleteMutation = useMutation({
    mutationFn: deleteScanProfile,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scan-profiles'] })
      toast.success('Scan profile deleted')
    },
    onError: () => toast.error('Failed to delete scan profile'),
  })

  const triggerMutation = useMutation({
    mutationFn: triggerScanRun,
    onSuccess: () => toast.success('Scan run triggered'),
    onError: () => toast.error('Failed to trigger scan run'),
  })

  const runnerName = (id: string) => runners.find((r) => r.id === id)?.name ?? '—'
  const connectionName = (id: string) => connections.find((c) => c.id === id)?.name ?? '—'

  const columns: ColumnDef<ScanProfile>[] = [
    { accessorKey: 'name', header: 'Name' },
    {
      id: 'schedule',
      header: 'Schedule',
      cell: ({ row }) => row.original.cronSchedule || <span className="text-muted-foreground">Manual</span>,
    },
    {
      id: 'runner',
      header: 'Runner',
      cell: ({ row }) => runnerName(row.original.scanRunnerId),
    },
    {
      id: 'connection',
      header: 'Connection',
      cell: ({ row }) => connectionName(row.original.connectionProfileId),
    },
    {
      id: 'tools',
      header: 'Tools',
      cell: ({ row }) => row.original.toolIds.length,
    },
    {
      id: 'enabled',
      header: 'Enabled',
      cell: ({ row }) =>
        row.original.enabled ? (
          <Badge variant="default" className="bg-green-600">Yes</Badge>
        ) : (
          <Badge variant="secondary">No</Badge>
        ),
    },
    {
      id: 'actions',
      cell: ({ row }) => (
        <div className="flex gap-1">
          <Button
            size="sm"
            variant="ghost"
            onClick={() => triggerMutation.mutate({ data: { id: row.original.id } })}
            disabled={triggerMutation.isPending}
            title="Trigger manual run"
          >
            <Play className="h-3 w-3" />
          </Button>
          <Button
            size="sm"
            variant="ghost"
            onClick={() => {
              setEditing(row.original)
              setDialogOpen(true)
            }}
          >
            <PenSquare className="h-3 w-3" />
          </Button>
          <Button
            size="sm"
            variant="ghost"
            className="text-destructive"
            onClick={() => {
              if (confirm(`Delete "${row.original.name}"?`)) {
                deleteMutation.mutate({ data: { id: row.original.id } })
              }
            }}
          >
            <Trash2 className="h-3 w-3" />
          </Button>
        </div>
      ),
    },
  ]

  return (
    <>
      <Card>
        <CardHeader className="flex flex-row items-center justify-between">
          <CardTitle>Scan Profiles</CardTitle>
          <Button
            size="sm"
            onClick={() => {
              setEditing(null)
              setDialogOpen(true)
            }}
          >
            <Plus className="mr-1 h-4 w-4" />
            New Profile
          </Button>
        </CardHeader>
        <CardContent>
          <DataTable columns={columns} data={query.data?.items ?? []} />
          <PaginationControls
            page={page}
            pageSize={pageSize}
            totalCount={query.data?.totalCount ?? 0}
            totalPages={query.data?.totalPages ?? 0}
            onPageChange={onPageChange}
            onPageSizeChange={() => {}}
          />
        </CardContent>
      </Card>

      <ScanProfileDialog
        open={dialogOpen}
        onOpenChange={(open) => {
          setDialogOpen(open)
          if (!open) setEditing(null)
        }}
        profile={editing}
        runners={runners}
        connections={connections}
        tools={tools}
        isPending={createMutation.isPending || updateMutation.isPending}
        onSubmit={(data) => {
          if (editing) {
            updateMutation.mutate({ data: { id: editing.id, ...data } })
          } else {
            createMutation.mutate({ data })
          }
        }}
      />
    </>
  )
}
