import { useMutation } from '@tanstack/react-query'
import { createFileRoute, useRouter } from '@tanstack/react-router'
import { fetchUsers } from '@/api/users.functions'
import { updateUserRoles } from '@/api/users.functions'
import { fetchTenants } from '@/api/settings.functions'
import { UserTable } from '@/components/features/admin/UserTable'

export const Route = createFileRoute('/_authed/admin/users')({
  loader: async () => {
    const [users, tenants] = await Promise.all([
      fetchUsers({ data: {} }),
      fetchTenants({ data: { page: 1, pageSize: 100 } }),
    ])

    return {
      users,
      tenants: tenants.items,
    }
  },
  component: UsersPage,
})

function UsersPage() {
  const data = Route.useLoaderData()
  const router = useRouter()
  const mutation = useMutation({
    mutationFn: async (payload: { userId: string; roles: Array<{ tenantId: string; role: string }> }) => {
      await updateUserRoles({
        data: {
          userId: payload.userId,
          roles: payload.roles,
        },
      })
    },
    onSuccess: () => { void router.invalidate() },
  })

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Users</h1>
      <UserTable
        users={data.users.items}
        totalCount={data.users.totalCount}
        isUpdatingRoles={mutation.isPending}
        tenants={data.tenants.map((tenant) => ({ id: tenant.id, name: tenant.name }))}
        onUpdateRoles={(userId, roles) => {
          mutation.mutate({ userId, roles })
        }}
      />
    </section>
  )
}
