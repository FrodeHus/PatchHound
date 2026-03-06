import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/admin/users')({
  component: AdminUsersPage,
})

function AdminUsersPage() {
  return (
    <section className="space-y-2">
      <h1 className="text-2xl font-semibold">User Management</h1>
      <p className="text-sm text-muted-foreground">Role and tenant assignment controls will be implemented later.</p>
    </section>
  )
}
