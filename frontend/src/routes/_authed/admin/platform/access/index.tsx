import { useQuery } from '@tanstack/react-query'
import { createFileRoute, redirect } from '@tanstack/react-router'
import { fetchTeams } from '@/api/teams.functions'
import { fetchUsers } from '@/api/users.functions'
import { UserTable } from '@/components/features/admin/UserTable'
import { baseListSearchSchema, searchStringSchema } from '@/routes/-list-search'

const userSearchSchema = baseListSearchSchema.extend({
  search: searchStringSchema,
  role: searchStringSchema,
  status: searchStringSchema,
  teamId: searchStringSchema,
})

export const Route = createFileRoute('/_authed/admin/platform/access/')({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes('GlobalAdmin') && !activeRoles.includes('CustomerAdmin')) {
      throw redirect({ to: '/admin' })
    }
  },
  validateSearch: userSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: async ({ deps }) => {
    const [users, teams] = await Promise.all([
      fetchUsers({
        data: {
          search: deps.search || undefined,
          role: deps.role || undefined,
          status: deps.status || undefined,
          teamId: deps.teamId || undefined,
          page: deps.page,
          pageSize: deps.pageSize,
        },
      }),
      fetchTeams({ data: { page: 1, pageSize: 200 } }),
    ])
    return { users, teams: teams.items }
  },
  component: AccessControlPage,
})

function AccessControlPage() {
  const data = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()

  const usersQuery = useQuery({
    queryKey: ['users', search],
    queryFn: () =>
      fetchUsers({
        data: {
          search: search.search || undefined,
          role: search.role || undefined,
          status: search.status || undefined,
          teamId: search.teamId || undefined,
          page: search.page,
          pageSize: search.pageSize,
        },
      }),
    initialData: data.users,
  })

  const teamsQuery = useQuery({
    queryKey: ['teams', 'user-admin'],
    queryFn: () => fetchTeams({ data: { page: 1, pageSize: 200 } }),
    initialData: {
      items: data.teams,
      totalCount: data.teams.length,
      page: 1,
      pageSize: 200,
      totalPages: 1,
    },
  })

  return (
    <section className="space-y-5">
      <div className="space-y-1">
        <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">
          Platform access
        </p>
        <h1 className="text-3xl font-semibold tracking-[-0.04em]">Access control</h1>
        <p className="max-w-3xl text-sm text-muted-foreground">
          Select an identity to review and edit their tenant reach, roles, and assignment groups.
        </p>
      </div>

      <UserTable
        users={usersQuery.data.items}
        totalCount={usersQuery.data.totalCount}
        page={usersQuery.data.page}
        pageSize={usersQuery.data.pageSize}
        totalPages={usersQuery.data.totalPages}
        selectedUserId={null}
        filters={{
          search: search.search,
          role: search.role,
          status: search.status,
          teamId: search.teamId,
        }}
        teams={teamsQuery.data.items}
        onFilterChange={(next) => {
          void navigate({
            search: (prev) => ({ ...prev, ...next, page: 1 }),
          })
        }}
        onPageChange={(page) => {
          void navigate({ search: (prev) => ({ ...prev, page }) })
        }}
        onPageSizeChange={(pageSize) => {
          void navigate({ search: (prev) => ({ ...prev, pageSize, page: 1 }) })
        }}
        onSelectUser={(userId) => {
          void navigate({
            to: '/admin/platform/access/$userId',
            params: { userId },
          })
        }}
      />
    </section>
  )
}
