import { createFileRoute } from '@tanstack/react-router'
import { TaskList } from '@/components/features/tasks/TaskList'
import { useTasks, useUpdateTaskStatus } from '@/api/useTasks'

export const Route = createFileRoute('/tasks/')({
  component: TasksPage,
})

function TasksPage() {
  const tasksQuery = useTasks({ page: 1, pageSize: 50 })
  const updateStatusMutation = useUpdateTaskStatus()

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">My Tasks</h1>

      {tasksQuery.isLoading ? <p className="text-sm text-muted-foreground">Loading tasks...</p> : null}
      {tasksQuery.isError ? <p className="text-sm text-destructive">Failed to load tasks.</p> : null}

      {tasksQuery.data ? (
        <TaskList
          tasks={tasksQuery.data.items}
          isUpdating={updateStatusMutation.isPending}
          onUpdateStatus={(taskId, status, justification) => {
            updateStatusMutation.mutate({ id: taskId, status, justification })
          }}
        />
      ) : null}
    </section>
  )
}
