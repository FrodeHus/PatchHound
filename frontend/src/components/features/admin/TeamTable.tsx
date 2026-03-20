import { useMemo } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import type { TeamItem } from '@/api/teams.schemas'
import { Badge } from '@/components/ui/badge'
import { SortableColumnHeader } from '@/components/ui/sortable-column-header'
import { DataTable } from '@/components/ui/data-table'
import {
  DataTableEmptyState,
  DataTableSummaryStrip,
  DataTableToolbar,
  DataTableToolbarRow,
  DataTableWorkbench,
} from '@/components/ui/data-table-workbench'
import { PaginationControls } from '@/components/ui/pagination-controls'

type TeamTableProps = {
  teams: TeamItem[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  selectedTeamId: string | null
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
  onSelectTeam: (teamId: string) => void
}

export function TeamTable({
  teams,
  totalCount,
  page,
  pageSize,
  totalPages,
  selectedTeamId,
  onPageChange,
  onPageSizeChange,
  onSelectTeam,
}: TeamTableProps) {
  const summaryItems = useMemo(() => {
    const activeOnPage = teams.filter((team) => team.id === selectedTeamId).length
    const totalMembers = teams.reduce((sum, team) => sum + team.memberCount, 0)

    return [
      { label: 'Groups on page', value: teams.length.toString(), tone: 'accent' as const },
      { label: 'Visible members', value: totalMembers.toString() },
      { label: 'Selected', value: activeOnPage.toString() },
    ]
  }, [selectedTeamId, teams])

  const columns = useMemo<ColumnDef<TeamItem>[]>(
    () => [
      {
        accessorKey: 'name',
        header: ({ column }) => <SortableColumnHeader column={column} title="Assignment group" />,
        cell: ({ row }) => (
          <button
            type="button"
            onClick={() => onSelectTeam(row.original.id)}
            className="space-y-1 text-left"
          >
            <div className="flex items-center gap-2">
              <span className="font-medium tracking-tight underline decoration-border/70 underline-offset-4 transition hover:decoration-foreground">
                {row.original.name}
              </span>
              {selectedTeamId === row.original.id ? (
                <Badge className="rounded-full border border-primary/20 bg-primary/10 text-primary hover:bg-primary/10">
                  Active
                </Badge>
              ) : null}
            </div>
            <p className="text-sm text-muted-foreground">{row.original.tenantName}</p>
          </button>
        ),
      },
      {
        accessorKey: 'tenantName',
        header: ({ column }) => <SortableColumnHeader column={column} title="Tenant" />,
        cell: ({ row }) => <span className="text-sm text-muted-foreground">{row.original.tenantName}</span>,
      },
      {
        accessorKey: 'memberCount',
        header: ({ column }) => <SortableColumnHeader column={column} title="Members" />,
        cell: ({ row }) => <span className="text-sm font-medium">{row.original.memberCount}</span>,
      },
      {
        id: 'selection',
        header: () => <div className="text-right">Open</div>,
        enableSorting: false,
        cell: ({ row }) => (
          <div className="text-right">
            <button
              type="button"
              onClick={() => onSelectTeam(row.original.id)}
              className="text-sm font-medium text-primary underline decoration-primary/30 underline-offset-4 transition hover:decoration-primary"
            >
              {selectedTeamId === row.original.id ? 'Selected' : 'Open workspace'}
            </button>
          </div>
        ),
      },
    ],
    [onSelectTeam, selectedTeamId],
  )

  return (
    <DataTableWorkbench
      title="Assignment Group Directory"
      description="Pick an assignment group to open its asset-assignment workspace."
      totalCount={totalCount}
    >
      <DataTableToolbar>
        <DataTableToolbarRow>
          <DataTableSummaryStrip items={summaryItems} className="grid-cols-1 sm:grid-cols-3" />
        </DataTableToolbarRow>
      </DataTableToolbar>

      {teams.length === 0 ? (
        <DataTableEmptyState
          title="No assignment groups found"
          description="Create a group first, then open it to start assigning assets."
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
