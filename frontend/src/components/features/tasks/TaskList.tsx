import { useMemo, useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import type { RemediationTask } from '@/api/tasks.schemas'
import { TaskStatusUpdate } from '@/components/features/tasks/TaskStatusUpdate'
import { Badge } from '@/components/ui/badge'
import { SortableColumnHeader } from '@/components/ui/sortable-column-header'
import { toneBadge } from '@/lib/tone-classes'
import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import {
  DataTableActiveFilters,
  DataTableEmptyState,
  DataTableField,
  DataTableSummaryStrip,
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
import { WorkbenchFilterDrawer, WorkbenchFilterSection } from '@/components/ui/workbench-filter-drawer'
import { taskListStatusOptions } from '@/lib/options/tasks'

type TaskListProps = {
  tasks: RemediationTask[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  statusFilter: string
  isUpdating: boolean
  onStatusFilterChange: (value: string) => void
  onApplyStructuredFilters: (filters: { status: string }) => void
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
  onClearFilters: () => void
  onUpdateStatus: (taskId: string, status: string, justification?: string) => void
}

function getDueSoon(task: RemediationTask): boolean {
  if (task.isOverdue) {
    return false
  }

  const now = Date.now()
  const due = new Date(task.dueDate).getTime()
  const diffDays = (due - now) / (1000 * 60 * 60 * 24)
  return diffDays <= 7
}

export function TaskList({
  tasks,
  totalCount,
  page,
  pageSize,
  totalPages,
  statusFilter,
  isUpdating,
  onStatusFilterChange,
  onApplyStructuredFilters,
  onPageChange,
  onPageSizeChange,
  onClearFilters,
  onUpdateStatus,
}: TaskListProps) {
  const [isFilterDrawerOpen, setIsFilterDrawerOpen] = useState(false)
  const [draftFilters, setDraftFilters] = useState({ status: statusFilter })

  const summaryItems = useMemo(() => {
    const overdue = tasks.filter((task) => task.isOverdue).length
    const dueSoon = tasks.filter((task) => !task.isOverdue && getDueSoon(task)).length
    const completed = tasks.filter((task) => task.status === 'Completed').length

    return [
      { label: 'Rows on page', value: tasks.length.toString(), tone: 'accent' as const },
      { label: 'Overdue', value: overdue.toString(), tone: 'warning' as const },
      { label: 'Due soon', value: dueSoon.toString() },
      { label: 'Completed', value: completed.toString() },
    ]
  }, [tasks])

  const columns = useMemo<ColumnDef<RemediationTask>[]>(
    () => [
      {
        accessorKey: 'vulnerabilityTitle',
        header: ({ column }) => <SortableColumnHeader column={column} title="Task" />,
        cell: ({ row }) => (
          <div className="space-y-1">
            <p className="font-medium tracking-tight">{row.original.vulnerabilityTitle}</p>
            <p className="text-xs text-muted-foreground">Asset: {row.original.assetName}</p>
          </div>
        ),
      },
      {
        accessorKey: 'status',
        header: ({ column }) => <SortableColumnHeader column={column} title="Status" />,
        cell: ({ row }) => (
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">
              {row.original.status}
            </Badge>
            {row.original.isOverdue ? (
              <Badge className="rounded-full border border-destructive/30 bg-destructive/10 text-destructive hover:bg-destructive/10">
                Overdue
              </Badge>
            ) : getDueSoon(row.original) ? (
              <Badge className={`rounded-full border ${toneBadge('warning')} hover:bg-tone-warning`}>
                Due soon
              </Badge>
            ) : (
              <Badge className={`rounded-full border ${toneBadge('success')} hover:bg-tone-success`}>
                On track
              </Badge>
            )}
          </div>
        ),
      },
      {
        accessorKey: 'dueDate',
        header: ({ column }) => <SortableColumnHeader column={column} title="Due" />,
        cell: ({ row }) => (
          <div className="space-y-1">
            <p className="text-sm text-muted-foreground">{new Date(row.original.dueDate).toLocaleString()}</p>
            <p className="text-xs text-muted-foreground">
              Created {new Date(row.original.createdAt).toLocaleDateString()}
            </p>
          </div>
        ),
      },
      {
        id: 'justification',
        header: 'Context',
        enableSorting: false,
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {row.original.justification?.trim() || 'No justification recorded'}
          </span>
        ),
      },
      {
        id: 'actions',
        header: () => <div className="text-right">Update</div>,
        enableSorting: false,
        cell: ({ row }) => (
          <div className="flex justify-end">
            <div className="min-w-[220px] rounded-2xl border border-border/60 bg-background/30 p-3">
              <TaskStatusUpdate
                currentStatus={row.original.status}
                isSubmitting={isUpdating}
                onSubmit={(status, justification) => {
                  onUpdateStatus(row.original.id, status, justification)
                }}
              />
            </div>
          </div>
        ),
      },
    ],
    [isUpdating, onUpdateStatus],
  )

  const activeFilters = useMemo(
    () =>
      [
        statusFilter
          ? {
              key: 'status',
              label: `Status: ${statusFilter}`,
              onClear: () => {
                onStatusFilterChange('')
              },
            }
          : null,
      ].filter((value): value is NonNullable<typeof value> => value !== null),
    [onStatusFilterChange, statusFilter],
  )

  const activeStructuredFilterCount = statusFilter ? 1 : 0

  return (
    <DataTableWorkbench
      title="Remediation Tasks"
      description="Work the active remediation queue, then move tasks across statuses without leaving the list."
      totalCount={totalCount}
    >
      <DataTableToolbar>
        <DataTableToolbarRow>
          <DataTableSummaryStrip items={summaryItems} className="flex-1" />
          <Button
            type="button"
            variant="outline"
            className="h-10 rounded-xl border-border/70 bg-background/80 px-4"
            onClick={() => {
              setDraftFilters({ status: statusFilter })
              setIsFilterDrawerOpen(true)
            }}
          >
            {activeStructuredFilterCount > 0 ? `Filters (${activeStructuredFilterCount})` : 'Filters...'}
          </Button>
        </DataTableToolbarRow>

        <DataTableToolbarRow>
          <DataTableActiveFilters filters={activeFilters} onClearAll={onClearFilters} className="flex-1" />
        </DataTableToolbarRow>
      </DataTableToolbar>

      <WorkbenchFilterDrawer
        open={isFilterDrawerOpen}
        onOpenChange={(open) => {
          setIsFilterDrawerOpen(open)
          if (!open) setDraftFilters({ status: statusFilter })
        }}
        title="Task Filters"
        description="Focus the remediation queue by task state while keeping the update workflow in view."
        activeCount={activeStructuredFilterCount}
        onResetDraft={() => {
          setDraftFilters({ status: '' })
        }}
        onApply={() => {
          onApplyStructuredFilters(draftFilters)
          setIsFilterDrawerOpen(false)
        }}
      >
        <WorkbenchFilterSection
          title="Status"
          description="Narrow the remediation queue to the stage you want to work through right now."
        >
          <DataTableField label="Status">
            <Select
              value={draftFilters.status || 'all'}
              onValueChange={(value) => {
                const nextValue = value ?? 'all'
                setDraftFilters({ status: nextValue === 'all' ? '' : nextValue })
              }}
            >
              <SelectTrigger className="h-10 w-full rounded-xl border-border/70 bg-background/80 px-3">
                <SelectValue placeholder="Any status" />
              </SelectTrigger>
              <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                <SelectItem value="all">Any status</SelectItem>
                {taskListStatusOptions.map((option) => (
                  <SelectItem key={option} value={option}>
                    {option}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </DataTableField>
        </WorkbenchFilterSection>
      </WorkbenchFilterDrawer>

      {tasks.length === 0 ? (
        <DataTableEmptyState
          title="No remediation tasks match the current view"
          description="Try widening the task status filter to bring more of the queue into scope."
        />
      ) : (
        <div className="overflow-hidden rounded-2xl border border-border/70 bg-background/30">
          <DataTable columns={columns} data={tasks} getRowId={(row) => row.id} className="min-w-[1220px]" />
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
