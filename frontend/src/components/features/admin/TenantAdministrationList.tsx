import { Link } from '@tanstack/react-router'
import { useMemo, useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import { Building2, ChevronRight, Plus } from 'lucide-react'
import type { TenantListItem } from '@/api/settings.schemas'
import { Badge } from '@/components/ui/badge'
import { SortableColumnHeader } from '@/components/ui/sortable-column-header'
import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import { Input } from '@/components/ui/input'
import { PaginationControls } from '@/components/ui/pagination-controls'
import {
  DataTableEmptyState,
  DataTableWorkbench,
} from '@/components/ui/data-table-workbench'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  DialogClose,
} from '@/components/ui/dialog'

type TenantAdministrationListProps = {
  tenants: TenantListItem[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  isCreating: boolean
  createError: string | null
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
  onCreate: (payload: { name: string; entraTenantId: string }) => Promise<unknown>
}

export function TenantAdministrationList({
  tenants,
  totalCount,
  page,
  pageSize,
  totalPages,
  isCreating,
  createError,
  onPageChange,
  onPageSizeChange,
  onCreate,
}: TenantAdministrationListProps) {
  const [name, setName] = useState('')
  const [entraTenantId, setEntraTenantId] = useState('')
  const [dialogOpen, setDialogOpen] = useState(false)

  const columns = useMemo<ColumnDef<TenantListItem>[]>(
    () => [
      {
        accessorKey: 'name',
        header: ({ column }) => <SortableColumnHeader column={column} title="Tenant" />,
        cell: ({ row }) => (
          <Link
            to="/admin/tenants/$id"
            params={{ id: row.original.id }}
            className="group flex items-center gap-3"
          >
            <div className="flex size-9 items-center justify-center rounded-lg border border-border/70 bg-muted/50">
              <Building2 className="size-4 text-muted-foreground" />
            </div>
            <div className="min-w-0">
              <p className="truncate font-medium tracking-tight group-hover:text-primary transition-colors">
                {row.original.name}
              </p>
              <p className="truncate font-mono text-[11px] text-muted-foreground">{row.original.entraTenantId}</p>
            </div>
          </Link>
        ),
      },
      {
        accessorKey: 'configuredIngestionSourceCount',
        header: ({ column }) => <SortableColumnHeader column={column} title="Sources" />,
        cell: ({ row }) => {
          const count = row.original.configuredIngestionSourceCount
          return (
            <Badge variant="outline" className="rounded-full border-border/70 bg-background/70 text-foreground">
              {count} {count === 1 ? 'source' : 'sources'}
            </Badge>
          )
        },
      },
      {
        id: 'actions',
        enableSorting: false,
        cell: ({ row }) => (
          <div className="text-right">
            <Link
              to="/admin/tenants/$id"
              params={{ id: row.original.id }}
              className="inline-flex items-center gap-1 text-sm text-muted-foreground transition hover:text-foreground"
            >
              <ChevronRight className="size-4" />
            </Link>
          </div>
        ),
      },
    ],
    [],
  )

  return (
    <section className="space-y-5">
      <div className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="space-y-2">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Administration</p>
            <h1 className="text-3xl font-semibold tracking-[-0.04em]">Tenants</h1>
            <p className="max-w-2xl text-sm text-muted-foreground">
              Manage tenant identity, review source configuration, and SLA policy.
            </p>
          </div>
          <Dialog open={dialogOpen} onOpenChange={(open) => { setDialogOpen(open); if (!open) { setName(''); setEntraTenantId('') } }}>
            <DialogTrigger render={<Button />}>
              <Plus className="mr-2 size-4" />
              Add tenant
            </DialogTrigger>
            <DialogContent>
              <DialogHeader>
                <DialogTitle>Add Tenant</DialogTitle>
                <DialogDescription>
                  PatchHound will create the tenant record, a default SLA policy, and a disabled Microsoft Defender source template.
                </DialogDescription>
              </DialogHeader>
              <div className="space-y-4 py-2">
                <label className="block space-y-2">
                  <span className="text-sm font-medium">Tenant name</span>
                  <Input
                    placeholder="Contoso Production"
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                  />
                </label>
                <label className="block space-y-2">
                  <span className="text-sm font-medium">Entra tenant ID</span>
                  <Input
                    placeholder="00000000-0000-0000-0000-000000000000"
                    value={entraTenantId}
                    onChange={(e) => setEntraTenantId(e.target.value)}
                  />
                  <p className="text-xs text-muted-foreground">Must be unique. Duplicate names or tenant IDs are rejected.</p>
                </label>
                {createError && (
                  <p className="text-sm text-destructive">{createError}</p>
                )}
              </div>
              <DialogFooter>
                <DialogClose render={<Button variant="outline" />}>
                  Cancel
                </DialogClose>
                <Button
                  disabled={isCreating || name.trim().length === 0 || entraTenantId.trim().length === 0}
                  onClick={() => {
                    void onCreate({
                      name: name.trim(),
                      entraTenantId: entraTenantId.trim(),
                    }).then(() => {
                      setName('')
                      setEntraTenantId('')
                      setDialogOpen(false)
                    }).catch(() => {})
                  }}
                >
                  {isCreating ? 'Creating...' : 'Add tenant'}
                </Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>
        </div>
      </div>

      <DataTableWorkbench
        title="Tenant Directory"
        description="Select a tenant to review identity, SLA, inventory footprint, and source readiness."
        totalCount={totalCount}
      >
        {tenants.length === 0 ? (
          <DataTableEmptyState
            title="No tenants registered"
            description="Add your first tenant to start configuring ingestion sources."
          />
        ) : (
          <div className="overflow-hidden rounded-2xl border border-border/80 bg-muted/55">
            <DataTable columns={columns} data={tenants} getRowId={(row) => row.id} />
          </div>
        )}

        <PaginationControls
          page={page}
          pageSize={pageSize}
          totalCount={totalCount}
          totalPages={totalPages}
          onPageChange={onPageChange}
          onPageSizeChange={onPageSizeChange}
        />
      </DataTableWorkbench>
    </section>
  )
}
