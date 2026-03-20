import { useMemo } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import type { UserItem } from '@/api/users.schemas'
import { ManageRolesDialog } from '@/components/features/admin/ManageRolesDialog'
import { Badge } from '@/components/ui/badge'
import { SortableColumnHeader } from '@/components/ui/sortable-column-header'
import {
  DataTableEmptyState,
  DataTableSummaryStrip,
  DataTableToolbar,
  DataTableToolbarRow,
  DataTableWorkbench,
} from '@/components/ui/data-table-workbench'
import { DataTable } from '@/components/ui/data-table'
import { PaginationControls } from '@/components/ui/pagination-controls'

type UserTableProps = {
  users: UserItem[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  isUpdatingRoles: boolean
  tenants: Array<{ id: string; name: string }>
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
  onUpdateRoles: (userId: string, roles: Array<{ tenantId: string; role: string }>) => void
}

export function UserTable({
  users,
  totalCount,
  page,
  pageSize,
  totalPages,
  isUpdatingRoles,
  tenants,
  onPageChange,
  onPageSizeChange,
  onUpdateRoles,
}: UserTableProps) {
  const summaryItems = useMemo(() => {
    const roleAssignments = users.reduce((sum, user) => sum + user.roles.length, 0)
    const multiTenantUsers = users.filter((user) => new Set(user.roles.map((role) => role.tenantId)).size > 1).length

    return [
      { label: 'Users on page', value: users.length.toString(), tone: 'accent' as const },
      { label: 'Visible role assignments', value: roleAssignments.toString() },
      { label: 'Multi-tenant users', value: multiTenantUsers.toString() },
    ]
  }, [users])

  const columns = useMemo<ColumnDef<UserItem>[]>(
    () => [
      {
        accessorKey: 'displayName',
        header: ({ column }) => <SortableColumnHeader column={column} title="User" />,
        cell: ({ row }) => (
          <div className="space-y-1">
            <p className="font-medium tracking-tight">{row.original.displayName}</p>
            <p className="text-xs text-muted-foreground">{row.original.id}</p>
          </div>
        ),
      },
      {
        accessorKey: 'email',
        header: ({ column }) => <SortableColumnHeader column={column} title="Email" />,
        cell: ({ row }) => <span className="text-sm text-muted-foreground">{row.original.email}</span>,
      },
      {
        id: 'roles',
        header: 'Roles',
        enableSorting: false,
        cell: ({ row }) => (
          <div className="flex flex-wrap gap-2">
            {row.original.roles.map((role) => (
              <Badge
                key={`${role.tenantId}-${role.role}`}
                variant="outline"
                className="rounded-full border-border/70 bg-background/70"
              >
                {role.tenantName}:{role.role}
              </Badge>
            ))}
          </div>
        ),
      },
      {
        id: 'actions',
        header: () => <div className="text-right">Manage</div>,
        enableSorting: false,
        cell: ({ row }) => (
          <div className="text-right">
            <ManageRolesDialog
              userId={row.original.id}
              isSubmitting={isUpdatingRoles}
              tenants={tenants}
              onUpdateRoles={onUpdateRoles}
            />
          </div>
        ),
      },
    ],
    [isUpdatingRoles, onUpdateRoles, tenants],
  )

  return (
    <DataTableWorkbench
      title="Users"
      description="Review tenant role coverage and update role assignments from one shared operator directory."
      totalCount={totalCount}
    >
      <DataTableToolbar>
        <DataTableToolbarRow>
          <DataTableSummaryStrip items={summaryItems} className="grid-cols-1 sm:grid-cols-3" />
        </DataTableToolbarRow>
      </DataTableToolbar>

      {users.length === 0 ? (
        <DataTableEmptyState
          title="No users found"
          description="Once authenticated users are provisioned and assigned roles, they will appear here."
        />
      ) : (
        <div className="overflow-hidden rounded-2xl border border-border/70 bg-background/30">
          <DataTable columns={columns} data={users} getRowId={(row) => row.id} className="min-w-[980px]" />
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
