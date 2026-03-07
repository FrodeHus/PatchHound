import { createFileRoute } from '@tanstack/react-router'
import { fetchTasks } from '@/api/tasks.functions'
import { TaskList } from '@/components/features/tasks/TaskList'

export const Route = createFileRoute('/_authed/tasks/')({
  loader: () => fetchTasks({ data: {} }),
  component: TasksPage,
})

function TasksPage() {
  const data = Route.useLoaderData()

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Remediation Tasks</h1>
      <TaskList data={data} />
    </section>
  )
}
