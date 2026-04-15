import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { createFileRoute, redirect } from '@tanstack/react-router'
import { toast } from 'sonner'
import { fetchTenants } from '@/api/settings.functions'
import { fetchTeams } from '@/api/teams.functions'
import { fetchUserAudit, fetchUserDetail, fetchUsers, updateUser } from '@/api/users.functions'
import { UserDetailPanel } from '@/components/features/admin/UserDetailPanel'
import { UserTable } from '@/components/features/admin/UserTable'
import { baseListSearchSchema, searchStringSchema } from '@/routes/-list-search'

const userSearchSchema = baseListSearchSchema.extend({
  search: searchStringSchema,
  role: searchStringSchema,
  status: searchStringSchema,
  teamId: searchStringSchema,
})

export const Route = createFileRoute('/_authed/admin/platform/access')({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes('GlobalAdmin') && !activeRoles.includes('CustomerAdmin')) {
      throw redirect({ to: '/admin' })
    }
  },
  validateSearch: userSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: async ({ deps }) => {
    const [users, teams, tenants] = await Promise.all([
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
      fetchTenants({ data: { page: 1, pageSize: 200 } }),
    ])

    const initialUserDetail = users.items[0]
      ? await fetchUserDetail({ data: { userId: users.items[0].id } })
      : null

    return {
      users,
      teams: teams.items,
      tenants: tenants.items,
      initialUserDetail,
    }
  },
  component: AccessControlPage,
})

function AccessControlPage() {
  const data = Route.useLoaderData()
  const { user } = Route.useRouteContext()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const [selectedUserId, setSelectedUserId] = useState<string | null>(data.users.items[0]?.id ?? null)
  const [auditFilters, setAuditFilters] = useState({
    entityType: '',
    action: '',
  })

  const usersQuery = useQuery({
    queryKey: ['users', search],
    queryFn: () => fetchUsers({
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
    initialData: { items: data.teams, totalCount: data.teams.length, page: 1, pageSize: 200, totalPages: 1 },
  })

  const effectiveSelectedUserId = usersQuery.data.items.some((u) => u.id === selectedUserId)
    ? selectedUserId
    : (usersQuery.data.items[0]?.id ?? null)

  const userDetailQuery = useQuery({
    queryKey: ['user-detail', effectiveSelectedUserId],
    queryFn: () => fetchUserDetail({ data: { userId: effectiveSelectedUserId! } }),
    enabled: Boolean(effectiveSelectedUserId),
    initialData:
      effectiveSelectedUserId === data.initialUserDetail?.id
        ? (data.initialUserDetail ?? undefined)
        : undefined,
  })

  const userAuditQuery = useQuery({
    queryKey: ['user-audit', effectiveSelectedUserId, auditFilters],
    queryFn: () => fetchUserAudit({
      data: {
        userId: effectiveSelectedUserId!,
        entityType: auditFilters.entityType || undefined,
        action: auditFilters.action || undefined,
        page: 1,
        pageSize: 50,
      },
    }),
    enabled: Boolean(effectiveSelectedUserId),
  })

  const updateMutation = useMutation({
    mutationFn: async (payload: {
      userId: string
      displayName: string
      email: string
      company: string | null
      isEnabled: boolean
      accessScope: string
      roles: string[]
      teamIds: string[]
      tenantAccess: Array<{ tenantId: string; roles: string[] }>
    }) => updateUser({ data: payload }),
    onSuccess: async () => {
      toast.success('User updated')
      await Promise.all([
        usersQuery.refetch(),
        userDetailQuery.refetch(),
        userAuditQuery.refetch(),
      ])
    },
    onError: () => {
      toast.error('Failed to update user')
    },
  })

  return (
    <section className="space-y-4">
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold">Access Control</h1>
        <p className="text-sm text-muted-foreground">
          Review platform users, update access, manage assignment groups, and inspect audit history.
        </p>
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,0.92fr)_minmax(0,1.08fr)]">
        <UserTable
          users={usersQuery.data.items}
          totalCount={usersQuery.data.totalCount}
          page={usersQuery.data.page}
          pageSize={usersQuery.data.pageSize}
          totalPages={usersQuery.data.totalPages}
          selectedUserId={effectiveSelectedUserId}
          filters={{
            search: search.search,
            role: search.role,
            status: search.status,
            teamId: search.teamId,
          }}
          teams={teamsQuery.data.items}
          onFilterChange={(next) => {
            void navigate({
              search: (prev) => ({
                ...prev,
                ...next,
                page: 1,
              }),
            })
          }}
          onPageChange={(page) => {
            void navigate({
              search: (prev) => ({
                ...prev,
                page,
              }),
            })
          }}
          onPageSizeChange={(pageSize) => {
            void navigate({
              search: (prev) => ({
                ...prev,
                pageSize,
                page: 1,
              }),
            })
          }}
          onSelectUser={(userId) => setSelectedUserId(userId)}
        />

        <UserDetailPanel
          user={userDetailQuery.data}
          currentUser={user}
          teams={teamsQuery.data.items}
          tenants={data.tenants}
          auditItems={userAuditQuery.data?.items ?? []}
          auditFilters={auditFilters}
          isLoading={userDetailQuery.isLoading}
          isSaving={updateMutation.isPending}
          onAuditFilterChange={setAuditFilters}
          onSave={(payload) => {
            if (!effectiveSelectedUserId) {
              return
            }

            updateMutation.mutate({
              userId: effectiveSelectedUserId,
              ...payload,
            })
          }}
        />
      </div>
    </section>
  )
}
