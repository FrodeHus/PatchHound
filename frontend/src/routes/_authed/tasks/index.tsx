import { createFileRoute } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { fetchTasks } from '@/api/tasks.functions'
import { updateTaskStatus } from '@/api/tasks.functions'
import { TaskList } from '@/components/features/tasks/TaskList'

export const Route = createFileRoute('/_authed/tasks/')({
  loader: () => fetchTasks({ data: {} }),
  component: TasksPage,
})

function TasksPage() {
  const data = Route.useLoaderData()
  const mutation = useMutation({
    mutationFn: async (payload: { taskId: string; status: string; justification?: string }) => {
      await updateTaskStatus({
        data: {
          id: payload.taskId,
          status: payload.status,
          justification: payload.justification,
        },
      })
    },
  })

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Remediation Tasks</h1>
      <TaskList
        tasks={data.items}
        isUpdating={mutation.isPending}
        onUpdateStatus={(taskId, status, justification) => {
          mutation.mutate({ taskId, status, justification })
        }}
      />
    </section>
  )
}
