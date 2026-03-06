import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/admin/teams')({
  component: AdminTeamsPage,
})

function AdminTeamsPage() {
  return (
    <section className="space-y-2">
      <h1 className="text-2xl font-semibold">Team Management</h1>
      <p className="text-sm text-muted-foreground">Team CRUD and membership management will be implemented later.</p>
    </section>
  )
}
