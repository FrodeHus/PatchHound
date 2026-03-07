import { createFileRoute } from '@tanstack/react-router'
import { fetchTeams } from '@/api/teams.functions'
import { TeamTable } from '@/components/features/admin/TeamTable'

export const Route = createFileRoute('/_authed/admin/teams')({
  loader: () => fetchTeams({ data: {} }),
  component: TeamsPage,
})

function TeamsPage() {
  const data = Route.useLoaderData()

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Teams</h1>
      <TeamTable data={data} />
    </section>
  )
}
