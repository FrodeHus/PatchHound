import { useMemo, useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import type { AuditLogItem } from '@/api/audit-log.schemas'
import { AuditDetailDialog } from '@/components/features/audit/AuditDetailDialog'
import { Badge } from '@/components/ui/badge'
import { SortableColumnHeader } from '@/components/ui/sortable-column-header'
import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import {
  DataTableActiveFilters,
  DataTableEmptyState,
  DataTableField,
  DataTableFilterBar,
  DataTableToolbar,
  DataTableToolbarRow,
  DataTableWorkbench,
} from '@/components/ui/data-table-workbench'
import { PaginationControls } from '@/components/ui/pagination-controls'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { formatAuditEntityType, formatAuditKey, parseAuditValues } from '@/lib/audit'
import { toneBadge } from '@/lib/tone-classes'
type AuditLogTableProps = {
  items: AuditLogItem[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  actionFilter: string
  entityTypeFilter: string
  onActionFilterChange: (action: string) => void
  onEntityTypeFilterChange: (entityType: string) => void
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
  onClearFilters: () => void
}

const actionOptions = ['Created', 'Updated', 'Deleted']

export function AuditLogTable({
  items,
  totalCount,
  page,
  pageSize,
  totalPages,
  actionFilter,
  entityTypeFilter,
  onActionFilterChange,
  onEntityTypeFilterChange,
  onPageChange,
  onPageSizeChange,
  onClearFilters,
}: AuditLogTableProps) {
  const [selected, setSelected] = useState<AuditLogItem | null>(null)

  const entityTypeOptions = useMemo(
    () =>
      Array.from(new Set(items.map((item) => item.entityType))).sort(
        (left, right) => left.localeCompare(right),
      ),
    [items],
  );  

  const activeFilters = useMemo(
    () =>
      [
        actionFilter
          ? {
              key: 'action',
              label: `Action: ${actionFilter}`,
              onClear: () => {
                onActionFilterChange('')
              },
            }
          : null,
        entityTypeFilter
          ? {
              key: 'entityType',
              label: `Entity: ${formatAuditEntityType(entityTypeFilter)}`,
              onClear: () => {
                onEntityTypeFilterChange('')
              },
            }
          : null,
      ].filter((value): value is NonNullable<typeof value> => value !== null),
    [actionFilter, entityTypeFilter, onActionFilterChange, onEntityTypeFilterChange],
  )

  const columns = useMemo<ColumnDef<AuditLogItem>[]>(
    () => [
      {
        accessorKey: 'timestamp',
        header: ({ column }) => <SortableColumnHeader column={column} title="Time" />,
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {new Date(row.original.timestamp).toLocaleString()}
          </span>
        ),
      },
      {
        accessorKey: 'action',
        header: ({ column }) => <SortableColumnHeader column={column} title="Action" />,
        cell: ({ row }) => <Badge className={actionBadgeClassName(row.original.action)}>{row.original.action}</Badge>,
      },
      {
        accessorKey: 'entityType',
        header: ({ column }) => <SortableColumnHeader column={column} title="Entity" />,
        cell: ({ row }) => (
          <div className="space-y-1">
            <p className="font-medium">{formatAuditEntityType(row.original.entityType)}</p>
            <p className="text-xs text-muted-foreground">{row.original.entityLabel ?? row.original.entityId}</p>
          </div>
        ),
      },
      {
        accessorKey: 'userDisplayName',
        header: ({ column }) => <SortableColumnHeader column={column} title="Actor" />,
        cell: ({ row }) => (
          <div className="space-y-1">
            <p className="font-medium">{row.original.userDisplayName ?? 'Unknown operator'}</p>
            <p className="text-xs text-muted-foreground">{row.original.userId}</p>
          </div>
        ),
      },
      {
        id: 'summary',
        header: 'Summary',
        enableSorting: false,
        cell: ({ row }) => <span className="text-sm text-muted-foreground">{summarizeEntry(row.original)}</span>,
      },
      {
        id: 'details',
        header: () => <div className="text-right">Details</div>,
        enableSorting: false,
        cell: ({ row }) => (
          <div className="text-right">
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => {
                setSelected(row.original)
              }}
            >
              View JSON
            </Button>
          </div>
        ),
      },
    ],
    [],
  )

  return (
    <>
      <DataTableWorkbench
        title="Audit Trail"
        description="Review administrative and configuration changes with actor, entity, and change-summary context."
        totalCount={totalCount}
      >
        <DataTableToolbar>
          <DataTableFilterBar className="lg:grid-cols-[repeat(2,minmax(220px,0.8fr))]">
            <DataTableField label="Action">
              <Select
                value={actionFilter || "all"}
                onValueChange={(value) => {
                  const nextValue = value ?? "all";
                  onActionFilterChange(nextValue === "all" ? "" : nextValue);
                }}
              >
                <SelectTrigger className="h-10 w-full rounded-xl border-border/70 bg-background/80 px-3">
                  <SelectValue placeholder="Any action" />
                </SelectTrigger>
                <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                  <SelectItem value="all">Any action</SelectItem>
                  {actionOptions.map((option) => (
                    <SelectItem key={option} value={option}>
                      {option}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </DataTableField>

            <DataTableField label="Entity type">
              <Select
                value={entityTypeFilter || "all"}
                onValueChange={(value) => {
                  const nextValue = value ?? "all";
                  onEntityTypeFilterChange(
                    nextValue === "all" ? "" : nextValue,
                  );
                }}
              >
                <SelectTrigger className="h-10 w-full rounded-xl border-border/70 bg-background/80 px-3">
                  <SelectValue placeholder="Any entity" />
                </SelectTrigger>
                <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                  <SelectItem value="all">Any entity</SelectItem>
                  {entityTypeOptions.map((option) => (
                    <SelectItem key={option} value={option}>
                      {formatAuditEntityType(option)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </DataTableField>
          </DataTableFilterBar>

          <DataTableToolbarRow>
            <DataTableActiveFilters
              filters={activeFilters}
              onClearAll={onClearFilters}
              className="flex-1"
            />
          </DataTableToolbarRow>
        </DataTableToolbar>

        {items.length === 0 ? (
          <DataTableEmptyState
            title="No audit entries match the current view"
            description="Try widening the action or entity-type filter to bring more history into scope."
          />
        ) : (
          <div className="overflow-hidden rounded-2xl border border-border/70 bg-background/30">
            <DataTable
              columns={columns}
              data={items}
              getRowId={(row) => row.id}
              className="min-w-[1120px]"
            />
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

      <AuditDetailDialog
        selected={selected}
        onClose={() => {
          setSelected(null);
        }}
      />
    </>
  );
}

function summarizeEntry(item: AuditLogItem) {
  const values = parseAuditValues(item.newValues)
  const previous = parseAuditValues(item.oldValues)
  const changedKeys = [...new Set([...Object.keys(values), ...Object.keys(previous)])]

  if (item.action === 'Created') {
    return 'Created entry.'
  }

  if (item.action === 'Deleted') {
    return 'Deleted entry.'
  }

  if (!changedKeys.length) {
    return 'Updated entry.'
  }

  const preview = changedKeys.slice(0, 3).map(formatAuditKey).join(', ')
  const suffix = changedKeys.length > 3 ? ` +${changedKeys.length - 3} more` : ''
  return `Changed ${preview}${suffix}`
}

function actionBadgeClassName(action: string) {
  switch (action) {
    case 'Created':
      return `rounded-full border ${toneBadge('success')} hover:bg-tone-success`
    case 'Updated':
      return 'rounded-full border border-primary/20 bg-primary/10 text-primary hover:bg-primary/10'
    case 'Deleted':
      return 'rounded-full border border-destructive/25 bg-destructive/10 text-destructive hover:bg-destructive/10'
    default:
      return 'rounded-full border border-border/70 bg-background/70 text-foreground hover:bg-background/70'
  }
}
