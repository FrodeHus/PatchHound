import { useState } from 'react'
import { createFileRoute, useRouter } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { createTeam, fetchTeams } from '@/api/teams.functions'
import { CreateTeamDialog } from '@/components/features/admin/CreateTeamDialog'
import { TeamTable } from '@/components/features/admin/TeamTable'

export const Route = createFileRoute('/_authed/admin/teams')({
  loader: () => fetchTeams({ data: {} }),
  component: TeamsPage,
})

function TeamsPage() {
  const router = useRouter()
  const data = Route.useLoaderData()
  const [createState, setCreateState] = useState<'idle' | 'success' | 'error'>('idle')

  const createMutation = useMutation({
    mutationFn: async (payload: { name: string; tenantId: string }) => {
      await createTeam({ data: payload })
    },
    onMutate: () => {
      setCreateState('idle')
    },
    onSuccess: async () => {
      setCreateState('success')
      await router.invalidate()
    },
    onError: () => {
      setCreateState('error')
    },
  })

  return (
    <section className="space-y-4">
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold">Assignment Groups</h1>
        <p className="text-sm text-muted-foreground">
          Group users by operational ownership so assets and workflows can be assigned consistently.
        </p>
      </div>
      <CreateTeamDialog
        isSubmitting={createMutation.isPending}
        onCreate={(payload) => {
          createMutation.mutate(payload)
        }}
      />
      {createState === 'success' ? (
        <p className="text-sm text-emerald-300">Assignment group created.</p>
      ) : null}
      {createState === 'error' ? (
        <p className="text-sm text-destructive">Failed to create assignment group.</p>
      ) : null}
      <TeamTable teams={data.items} totalCount={data.totalCount} />
    </section>
  )
}
