import { useState } from 'react'
import { createFileRoute, useRouter } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { toast } from 'sonner'
import { fetchTenants } from '@/api/settings.functions'
import { createTeam, fetchTeams } from '@/api/teams.functions'
import { CreateTeamDialog } from '@/components/features/admin/CreateTeamDialog'
import { TeamTable } from '@/components/features/admin/TeamTable'
import { baseListSearchSchema } from '@/routes/-list-search'

export const Route = createFileRoute('/_authed/admin/teams/')({
  validateSearch: baseListSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: async ({ deps, context }) => {
    const isGlobalAdmin = context.user?.roles.includes('GlobalAdmin') ?? false
    const [teams, tenants] = await Promise.all([
      fetchTeams({ data: { page: deps.page, pageSize: deps.pageSize } }),
      isGlobalAdmin ? fetchTenants({ data: { page: 1, pageSize: 100 } }) : Promise.resolve({ items: [] }),
    ])

    return {
      teams,
      tenants: tenants.items,
    }
  },
  component: TeamsPage,
})

function TeamsPage() {
  const router = useRouter()
  const data = Route.useLoaderData()
  const navigate = Route.useNavigate()
  const { user } = Route.useRouteContext()
  const [createState, setCreateState] = useState<'idle' | 'success' | 'error'>('idle')
  const canManageGroup = (user.activeRoles ?? []).includes('GlobalAdmin')

  const createMutation = useMutation({
    mutationFn: async (payload: { name: string; tenantId: string }) => {
      await createTeam({ data: payload })
    },
    onMutate: () => {
      setCreateState('idle')
    },
    onSuccess: async () => {
      setCreateState('success')
      toast.success('Assignment group created')
      await router.invalidate()
    },
    onError: () => {
      setCreateState('error')
      toast.error('Failed to create assignment group')
    },
  })

  return (
    <section className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="space-y-1">
          <h1 className="text-2xl font-semibold tracking-[-0.04em]">Assignment Groups</h1>
          <p className="max-w-3xl text-sm text-muted-foreground">
            Organize operational ownership by tenant, then open a dedicated group page to manage members, dynamic rules, and settings.
          </p>
        </div>
        {canManageGroup ? (
          <CreateTeamDialog
            isSubmitting={createMutation.isPending}
            tenants={data.tenants.map((tenant) => ({ id: tenant.id, name: tenant.name }))}
            onCreate={(payload) => {
              createMutation.mutate(payload)
            }}
          />
        ) : null}
      </div>
      {createState === 'success' ? (
        <p className="text-sm text-tone-success-foreground">Assignment group created.</p>
      ) : null}
      {createState === 'error' ? (
        <p className="text-sm text-destructive">Failed to create assignment group.</p>
      ) : null}
      <TeamTable
        teams={data.teams.items}
        totalCount={data.teams.totalCount}
        page={data.teams.page}
        pageSize={data.teams.pageSize}
        totalPages={data.teams.totalPages}
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
      />
    </section>
  )
}
