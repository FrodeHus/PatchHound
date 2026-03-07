import { createFileRoute } from '@tanstack/react-router'
import { fetchUsers } from '@/api/users.functions'
import { UserTable } from '@/components/features/admin/UserTable'

export const Route = createFileRoute('/_authed/admin/users')({
  loader: () => fetchUsers({ data: {} }),
  component: UsersPage,
})

function UsersPage() {
  const data = Route.useLoaderData()

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Users</h1>
      <UserTable data={data} />
    </section>
  )
}
