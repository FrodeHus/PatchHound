import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/tasks/')({
  component: TasksPage,
})

function TasksPage() {
  return (
    <section className="space-y-2">
      <h1 className="text-2xl font-semibold">My Tasks</h1>
      <p className="text-sm text-muted-foreground">Task workflow view is pending implementation.</p>
    </section>
  )
}
