import { useMutation } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchUsers } from '@/api/users.functions'
import { updateUserRoles } from '@/api/users.functions'
import { UserTable } from '@/components/features/admin/UserTable'

export const Route = createFileRoute('/_authed/admin/users')({
  loader: () => fetchUsers({ data: {} }),
  component: UsersPage,
})

function UsersPage() {
  const data = Route.useLoaderData()
  const mutation = useMutation({
    mutationFn: async (payload: { userId: string; roles: Array<{ tenantId: string; role: string }> }) => {
      await updateUserRoles({
        data: {
          userId: payload.userId,
          roles: payload.roles,
        },
      })
    },
  })

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Users</h1>
      <UserTable
        users={data.items}
        totalCount={data.totalCount}
        isUpdatingRoles={mutation.isPending}
        onUpdateRoles={(userId, roles) => {
          mutation.mutate({ userId, roles })
        }}
      />
    </section>
  )
}
