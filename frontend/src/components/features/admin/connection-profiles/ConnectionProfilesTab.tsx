import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { toast } from 'sonner'
import { PenSquare, Plus, Trash2 } from 'lucide-react'
import {
  createConnectionProfile,
  deleteConnectionProfile,
  fetchConnectionProfiles,
  updateConnectionProfile,
} from '@/api/authenticated-scans.functions'
import type { ConnectionProfile, PagedConnectionProfiles } from '@/api/authenticated-scans.schemas'
import { ConnectionProfileDialog } from './ConnectionProfileDialog'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { DataTable } from '@/components/ui/data-table'
import { PaginationControls } from '@/components/ui/pagination-controls'

type Props = {
  initialData: PagedConnectionProfiles
  page: number
  pageSize: number
  onPageChange: (page: number) => void
  onPageSizeChange?: (pageSize: number) => void
}

export function ConnectionProfilesTab({ initialData, page, pageSize, onPageChange, onPageSizeChange }: Props) {
  const queryClient = useQueryClient()
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editing, setEditing] = useState<ConnectionProfile | null>(null)

  const query = useQuery({
    queryKey: ['connection-profiles', page, pageSize],
    queryFn: () => fetchConnectionProfiles({ data: { page, pageSize } }),
    initialData,
  })

  const createMutation = useMutation({
    mutationFn: createConnectionProfile,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['connection-profiles'] })
      setDialogOpen(false)
      toast.success('Connection profile created')
    },
    onError: () => toast.error('Failed to create connection profile'),
  })

  const updateMutation = useMutation({
    mutationFn: updateConnectionProfile,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['connection-profiles'] })
      setDialogOpen(false)
      setEditing(null)
      toast.success('Connection profile updated')
    },
    onError: () => toast.error('Failed to update connection profile'),
  })

  const deleteMutation = useMutation({
    mutationFn: deleteConnectionProfile,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['connection-profiles'] })
      toast.success('Connection profile deleted')
    },
    onError: () => toast.error('Failed to delete connection profile'),
  })

  const columns: ColumnDef<ConnectionProfile>[] = [
    { accessorKey: 'name', header: 'Name' },
    {
      id: 'host',
      header: 'Host',
      cell: ({ row }) => `${row.original.sshHost}:${row.original.sshPort}`,
    },
    { accessorKey: 'sshUsername', header: 'Username' },
    {
      accessorKey: 'authMethod',
      header: 'Auth',
      cell: ({ row }) => (
        <Badge variant="outline">{row.original.authMethod === 'privateKey' ? 'Key' : 'Password'}</Badge>
      ),
    },
    {
      id: 'actions',
      cell: ({ row }) => (
        <div className="flex gap-1">
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
          <CardTitle>Connection Profiles</CardTitle>
          <Button
            size="sm"
            onClick={() => {
              setEditing(null)
              setDialogOpen(true)
            }}
          >
            <Plus className="mr-1 h-4 w-4" />
            New Connection
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

      <ConnectionProfileDialog
        key={editing?.id ?? 'new'}
        open={dialogOpen}
        onOpenChange={(open) => {
          setDialogOpen(open)
          if (!open) setEditing(null)
        }}
        profile={editing}
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
