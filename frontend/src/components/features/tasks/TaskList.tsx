import type { RemediationTask } from '@/api/tasks.schemas'
import { TaskStatusUpdate } from '@/components/features/tasks/TaskStatusUpdate'
import { PaginationControls } from '@/components/ui/pagination-controls'

type TaskListProps = {
  tasks: RemediationTask[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  isUpdating: boolean
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
  onUpdateStatus: (taskId: string, status: string, justification?: string) => void
}

type TaskGroup = {
  title: string
  description: string
  className: string
  items: RemediationTask[]
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

function groupTasks(tasks: RemediationTask[]): TaskGroup[] {
  const overdue = tasks.filter((task) => task.isOverdue)
  const dueSoon = tasks.filter((task) => !task.isOverdue && getDueSoon(task))
  const onTrack = tasks.filter((task) => !task.isOverdue && !getDueSoon(task))

  return [
    {
      title: 'Overdue',
      description: 'Past due date and not completed/accepted.',
      className: 'border-destructive/40 bg-destructive/5',
      items: overdue,
    },
    {
      title: 'Due Soon',
      description: 'Due in the next 7 days.',
      className: 'border-amber-500/40 bg-amber-500/5',
      items: dueSoon,
    },
    {
      title: 'On Track',
      description: 'Due date is more than 7 days away.',
      className: 'border-emerald-600/40 bg-emerald-600/5',
      items: onTrack,
    },
  ]
}

export function TaskList({
  tasks,
  totalCount,
  page,
  pageSize,
  totalPages,
  isUpdating,
  onPageChange,
  onPageSizeChange,
  onUpdateStatus,
}: TaskListProps) {
  const groups = groupTasks(tasks)

  return (
    <div className="space-y-4">
      {groups.map((group) => (
        <section key={group.title} className={['rounded-lg border p-4', group.className].join(' ')}>
          <div className="mb-3">
            <h2 className="text-lg font-semibold">{group.title}</h2>
            <p className="text-xs text-muted-foreground">{group.description}</p>
          </div>

          <div className="space-y-3">
            {group.items.length === 0 ? (
              <p className="text-sm text-muted-foreground">No tasks in this category.</p>
            ) : (
              group.items.map((task) => (
                <article key={task.id} className="rounded-md border border-border bg-card p-3">
                  <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <p className="font-medium">{task.vulnerabilityTitle}</p>
                      <p className="text-xs text-muted-foreground">Asset: {task.assetName}</p>
                    </div>
                    <p className="text-xs text-muted-foreground">Due: {new Date(task.dueDate).toLocaleString()}</p>
                  </div>

                  <TaskStatusUpdate
                    currentStatus={task.status}
                    isSubmitting={isUpdating}
                    onSubmit={(status, justification) => {
                      onUpdateStatus(task.id, status, justification)
                    }}
                  />
                </article>
              ))
            )}
          </div>
        </section>
      ))}
      <PaginationControls
        page={page}
        pageSize={pageSize}
        totalCount={totalCount}
        totalPages={totalPages}
        onPageChange={onPageChange}
        onPageSizeChange={onPageSizeChange}
      />
    </div>
  )
}
