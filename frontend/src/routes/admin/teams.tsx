import { createFileRoute } from '@tanstack/react-router'
import { CreateTeamDialog } from '@/components/features/admin/CreateTeamDialog'
import { TeamTable } from '@/components/features/admin/TeamTable'
import { useCreateTeam, useTeams } from '@/api/useTeams'

export const Route = createFileRoute('/admin/teams')({
  component: AdminTeamsPage,
})

function AdminTeamsPage() {
  const teamsQuery = useTeams(undefined, 1, 100)
  const createMutation = useCreateTeam()

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Team Management</h1>

      <CreateTeamDialog
        isSubmitting={createMutation.isPending}
        onCreate={(payload) => {
          createMutation.mutate(payload)
        }}
      />

      {teamsQuery.isLoading ? <p className="text-sm text-muted-foreground">Loading teams...</p> : null}
      {teamsQuery.isError ? <p className="text-sm text-destructive">Failed to load teams.</p> : null}

      {teamsQuery.data ? <TeamTable teams={teamsQuery.data.items} totalCount={teamsQuery.data.totalCount} /> : null}
    </section>
  )
}
