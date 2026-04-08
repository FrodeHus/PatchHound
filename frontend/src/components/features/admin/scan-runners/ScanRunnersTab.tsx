import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { toast } from 'sonner'
import { Plus, RotateCw, Trash2 } from 'lucide-react'
import { formatDateTime } from '@/lib/formatting'
import {
  createScanRunner,
  deleteScanRunner,
  fetchScanRunners,
  rotateScanRunnerSecret,
} from '@/api/authenticated-scans.functions'
import type { PagedScanRunners, ScanRunner } from '@/api/authenticated-scans.schemas'
import { RunnerSecretModal } from './RunnerSecretModal'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { PaginationControls } from '@/components/ui/pagination-controls'
import { Textarea } from '@/components/ui/textarea'

type Props = {
  initialData: PagedScanRunners
  page: number
  pageSize: number
  onPageChange: (page: number) => void
  onPageSizeChange?: (pageSize: number) => void
}

export function ScanRunnersTab({ initialData, page, pageSize, onPageChange, onPageSizeChange }: Props) {
  const queryClient = useQueryClient()
  const [createOpen, setCreateOpen] = useState(false)
  const [secretModal, setSecretModal] = useState<{
    open: boolean
    runnerName: string
    runnerId: string
    bearerSecret: string
  }>({ open: false, runnerName: '', runnerId: '', bearerSecret: '' })
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')

  const query = useQuery({
    queryKey: ['scan-runners', page, pageSize],
    queryFn: () => fetchScanRunners({ data: { page, pageSize } }),
    initialData,
  })

  const createMutation = useMutation({
    mutationFn: createScanRunner,
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ['scan-runners'] })
      setCreateOpen(false)
      setName('')
      setDescription('')
      setSecretModal({
        open: true,
        runnerName: result.runner.name,
        runnerId: result.runner.id,
        bearerSecret: result.bearerSecret,
      })
    },
    onError: () => toast.error('Failed to create runner'),
  })

  const deleteMutation = useMutation({
    mutationFn: deleteScanRunner,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['scan-runners'] })
      toast.success('Runner deleted')
    },
    onError: () => toast.error('Failed to delete runner'),
  })

  const rotateMutation = useMutation({
    mutationFn: rotateScanRunnerSecret,
    onSuccess: (result, variables) => {
      const runner = query.data?.items.find((r) => r.id === variables.data.id)
      setSecretModal({
        open: true,
        runnerName: runner?.name ?? 'Runner',
        runnerId: variables.data.id,
        bearerSecret: result.bearerSecret,
      })
    },
    onError: () => toast.error('Failed to rotate secret'),
  })

  const isOnline = (runner: ScanRunner) => {
    if (!runner.lastSeenAt) return false
    const diff = Date.now() - new Date(runner.lastSeenAt).getTime()
    return diff < 2 * 60 * 1000
  }

  const columns: ColumnDef<ScanRunner>[] = [
    { accessorKey: 'name', header: 'Name' },
    {
      accessorKey: 'lastSeenAt',
      header: 'Last Seen',
      cell: ({ row }) => {
        const val = row.original.lastSeenAt
        if (!val) return <span className="text-muted-foreground">Never</span>
        return formatDateTime(val)
      },
    },
    { accessorKey: 'version', header: 'Version' },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) =>
        isOnline(row.original) ? (
          <Badge variant="default" className="bg-green-600">Online</Badge>
        ) : (
          <Badge variant="secondary">Offline</Badge>
        ),
    },
    {
      id: 'enabled',
      header: 'Enabled',
      cell: ({ row }) => (row.original.enabled ? 'Yes' : 'No'),
    },
    {
      id: 'actions',
      cell: ({ row }) => (
        <div className="flex gap-1">
          <Button
            size="sm"
            variant="ghost"
            onClick={() => {
              if (confirm(`Rotate secret for "${row.original.name}"? The current token will be invalidated immediately.`)) {
                rotateMutation.mutate({ data: { id: row.original.id } })
              }
            }}
            disabled={rotateMutation.isPending}
          >
            <RotateCw className="mr-1 h-3 w-3" />
            Rotate Secret
          </Button>
          <Button
            size="sm"
            variant="ghost"
            className="text-destructive"
            onClick={() => {
              if (confirm(`Delete runner "${row.original.name}"?`)) {
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
          <CardTitle>Scan Runners</CardTitle>
          <Button size="sm" onClick={() => setCreateOpen(true)}>
            <Plus className="mr-1 h-4 w-4" />
            New Runner
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
            onPageSizeChange={onPageSizeChange ?? (() => {})}
          />
        </CardContent>
      </Card>

      {/* Create dialog */}
      <Dialog open={createOpen} onOpenChange={setCreateOpen}>
        <DialogContent size="lg">
          <DialogHeader>
            <DialogTitle>New Scan Runner</DialogTitle>
            <DialogDescription>
              Create a runner identity. A bearer secret will be shown once after creation.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4">
            <div>
              <Label htmlFor="runner-name">Name</Label>
              <Input id="runner-name" value={name} onChange={(e) => setName(e.target.value)} placeholder="prod-scanner-01" />
            </div>
            <div>
              <Label htmlFor="runner-desc">Description</Label>
              <Textarea id="runner-desc" value={description} onChange={(e) => setDescription(e.target.value)} placeholder="On-prem runner in datacenter A" />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setCreateOpen(false)}>Cancel</Button>
            <Button
              onClick={() => createMutation.mutate({ data: { name, description } })}
              disabled={!name || createMutation.isPending}
            >
              Create Runner
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Secret modal */}
      <RunnerSecretModal
        open={secretModal.open}
        onOpenChange={(open) => setSecretModal((s) => ({ ...s, open }))}
        runnerName={secretModal.runnerName}
        runnerId={secretModal.runnerId}
        bearerSecret={secretModal.bearerSecret}
      />
    </>
  )
}
