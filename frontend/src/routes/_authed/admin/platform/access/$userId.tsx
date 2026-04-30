import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { createFileRoute, redirect, useNavigate } from '@tanstack/react-router'
import { ArrowLeft } from 'lucide-react'
import { toast } from 'sonner'
import { fetchTenants } from '@/api/settings.functions'
import { fetchTeams } from '@/api/teams.functions'
import { fetchUserAudit, fetchUserDetail, updateUser } from '@/api/users.functions'
import { UserDetailPanel } from '@/components/features/admin/UserDetailPanel'

export const Route = createFileRoute('/_authed/admin/platform/access/$userId')({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes('GlobalAdmin') && !activeRoles.includes('CustomerAdmin')) {
      throw redirect({ to: '/admin' })
    }
  },
  loader: async ({ params }) => {
    const [user, teams, tenants] = await Promise.all([
      fetchUserDetail({ data: { userId: params.userId } }),
      fetchTeams({ data: { page: 1, pageSize: 200 } }),
      fetchTenants({ data: { page: 1, pageSize: 200 } }),
    ])
    return { user, teams: teams.items, tenants: tenants.items }
  },
  component: UserDetailRoute,
})

function UserDetailRoute() {
  const data = Route.useLoaderData()
  const { userId } = Route.useParams()
  const { user: currentUser } = Route.useRouteContext()
  const navigate = useNavigate()
  const [auditFilters, setAuditFilters] = useState({ entityType: '', action: '' })

  const userDetailQuery = useQuery({
    queryKey: ['user-detail', userId],
    queryFn: () => fetchUserDetail({ data: { userId } }),
    initialData: data.user,
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

  const userAuditQuery = useQuery({
    queryKey: ['user-audit', userId, auditFilters],
    queryFn: () =>
      fetchUserAudit({
        data: {
          userId,
          entityType: auditFilters.entityType || undefined,
          action: auditFilters.action || undefined,
          page: 1,
          pageSize: 50,
        },
      }),
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
      await Promise.all([userDetailQuery.refetch(), userAuditQuery.refetch()])
    },
    onError: () => {
      toast.error('Failed to update user')
    },
  })

  return (
    <section className="space-y-6">
      <div className="space-y-1">
        <button
          type="button"
          onClick={() =>
            void navigate({
              to: '/admin/platform/access',
              search: { search: '', role: '', status: '', teamId: '', page: 1, pageSize: 25 },
            })
          }
          className="inline-flex items-center gap-1.5 text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground transition-colors hover:text-foreground"
        >
          <ArrowLeft className="size-3" />
          Access control
        </button>
        <h1 className="text-3xl font-semibold tracking-[-0.04em]">
          {userDetailQuery.data?.displayName ?? '—'}
        </h1>
        {userDetailQuery.data?.email ? (
          <p className="text-sm text-muted-foreground">{userDetailQuery.data.email}</p>
        ) : null}
      </div>

      <UserDetailPanel
        user={userDetailQuery.data}
        currentUser={currentUser}
        teams={teamsQuery.data.items}
        tenants={data.tenants}
        auditItems={userAuditQuery.data?.items ?? []}
        auditFilters={auditFilters}
        isLoading={userDetailQuery.isLoading}
        isSaving={updateMutation.isPending}
        onAuditFilterChange={setAuditFilters}
        onSaveAccess={(accessPayload) => {
          if (!userDetailQuery.data) {
            return
          }
          updateMutation.mutate({
            userId,
            ...accessPayload,
            teamIds: userDetailQuery.data.teams.map((t) => t.teamId),
          })
        }}
        onSaveGroups={(groupsPayload) => {
          if (!userDetailQuery.data) {
            return
          }
          const u = userDetailQuery.data
          updateMutation.mutate({
            userId,
            displayName: u.displayName,
            email: u.email,
            company: u.company,
            isEnabled: u.isEnabled,
            accessScope: u.accessScope,
            roles: u.roles,
            tenantAccess: u.tenantAccess.map((item) => ({
              tenantId: item.tenantId,
              roles: item.roles,
            })),
            ...groupsPayload,
          })
        }}
      />
    </section>
  )
}
