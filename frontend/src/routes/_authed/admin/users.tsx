import { useMutation, useQuery } from '@tanstack/react-query'
import { createFileRoute, useRouter } from '@tanstack/react-router'
import { fetchUsers } from '@/api/users.functions'
import { updateUserRoles } from '@/api/users.functions'
import { fetchTenants } from '@/api/settings.functions'
import { UserTable } from '@/components/features/admin/UserTable'
import { baseListSearchSchema } from '@/routes/-list-search'

export const Route = createFileRoute('/_authed/admin/users')({
  validateSearch: baseListSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: async ({ deps }) => {
    const [users, tenants] = await Promise.all([
      fetchUsers({ data: { page: deps.page, pageSize: deps.pageSize } }),
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
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const router = useRouter()
  const usersQuery = useQuery({
    queryKey: ['users', search.page, search.pageSize],
    queryFn: () => fetchUsers({ data: { page: search.page, pageSize: search.pageSize } }),
    initialData: data.users,
  })
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
        users={usersQuery.data.items}
        totalCount={usersQuery.data.totalCount}
        page={usersQuery.data.page}
        pageSize={usersQuery.data.pageSize}
        totalPages={usersQuery.data.totalPages}
        isUpdatingRoles={mutation.isPending}
        tenants={data.tenants.map((tenant) => ({ id: tenant.id, name: tenant.name }))}
        onPageChange={(page) => {
          void navigate({
            search: (prev) => ({ ...prev, page }),
          })
        }}
        onPageSizeChange={(nextPageSize) => {
          void navigate({
            search: (prev) => ({ ...prev, pageSize: nextPageSize, page: 1 }),
          })
        }}
        onUpdateRoles={(userId, roles) => {
          mutation.mutate({ userId, roles })
        }}
      />
    </section>
  )
}
