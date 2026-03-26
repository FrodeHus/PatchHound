import { useMemo } from 'react'
import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import type { TeamItem } from '@/api/teams.schemas'
import { Badge } from '@/components/ui/badge'
import { DataTable } from '@/components/ui/data-table'
import { DataTableEmptyState, DataTableWorkbench } from '@/components/ui/data-table-workbench'
import { PaginationControls } from '@/components/ui/pagination-controls'
import { SortableColumnHeader } from '@/components/ui/sortable-column-header'

type TeamTableProps = {
  teams: TeamItem[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
}

export function TeamTable({
  teams,
  totalCount,
  page,
  pageSize,
  totalPages,
  onPageChange,
  onPageSizeChange,
}: TeamTableProps) {
  const columns = useMemo<ColumnDef<TeamItem>[]>(
    () => [
      {
        accessorKey: 'name',
        header: ({ column }) => <SortableColumnHeader column={column} title="Assignment group" />,
        cell: ({ row }) => (
          <div className="space-y-1">
            <div className="flex flex-wrap items-center gap-2">
              <span className="font-medium tracking-tight">{row.original.name}</span>
              {row.original.isDefault ? (
                <Badge className="rounded-full border border-amber-300/60 bg-amber-500/10 text-amber-700 hover:bg-amber-500/10 dark:text-amber-300">
                  Fallback
                </Badge>
              ) : null}
              {row.original.isDynamic ? (
                <Badge className="rounded-full border border-primary/20 bg-primary/10 text-primary hover:bg-primary/10">
                  Dynamic
                </Badge>
              ) : (
                <Badge variant="outline" className="rounded-full border-border/70 bg-background/50 text-muted-foreground">
                  Static
                </Badge>
              )}
            </div>
            <p className="text-sm text-muted-foreground">{row.original.tenantName}</p>
          </div>
        ),
      },
      {
        accessorKey: 'tenantName',
        header: ({ column }) => <SortableColumnHeader column={column} title="Tenant" />,
        cell: ({ row }) => <span className="text-sm text-muted-foreground">{row.original.tenantName}</span>,
      },
      {
        id: 'mode',
        header: () => <span>Mode</span>,
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {row.original.isDynamic ? 'Dynamic rule sync' : 'Manual membership'}
          </span>
        ),
      },
      {
        accessorKey: 'memberCount',
        header: ({ column }) => <SortableColumnHeader column={column} title="Members" />,
        cell: ({ row }) => <span className="text-sm font-medium">{row.original.memberCount}</span>,
      },
      {
        accessorKey: 'currentRiskScore',
        header: ({ column }) => <SortableColumnHeader column={column} title="Current risk" />,
        cell: ({ row }) => (
          <span className="text-sm font-medium">
            {typeof row.original.currentRiskScore === 'number' ? row.original.currentRiskScore.toFixed(0) : '0'}
          </span>
        ),
      },
      {
        id: 'open',
        header: () => <div className="text-right">Open</div>,
        cell: ({ row }) => (
          <div className="text-right">
            <Link
              to="/admin/teams/$id"
              params={{ id: row.original.id }}
              className="text-sm font-medium text-primary underline decoration-primary/30 underline-offset-4 transition hover:decoration-primary"
            >
              Open
            </Link>
          </div>
        ),
      },
    ],
    [],
  )

  return (
    <DataTableWorkbench
      title="Assignment Group Directory"
      description="Browse groups, search by tenant and mode, then open a dedicated group page to manage membership, rules, and settings."
      totalCount={totalCount}
    >
      {teams.length === 0 ? (
        <DataTableEmptyState
          title="No assignment groups found"
          description="Create a group first, then open it to manage members and rules."
        />
      ) : (
        <div className="overflow-hidden rounded-2xl border border-border/70 bg-background/30">
          <DataTable columns={columns} data={teams} getRowId={(row) => row.id} className="min-w-[860px]" />
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
  )
}
