import type { TeamItem } from '@/api/teams.schemas'

type TeamTableProps = {
  teams: TeamItem[]
  totalCount: number
}

export function TeamTable({ teams, totalCount }: TeamTableProps) {
  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <div className="mb-3 flex items-end justify-between">
        <h2 className="text-lg font-semibold">Assignment Groups</h2>
        <p className="text-xs text-muted-foreground">{totalCount} total</p>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[720px] border-collapse text-sm">
          <thead>
            <tr className="border-b border-border text-left text-muted-foreground">
              <th className="py-2 pr-2">Name</th>
              <th className="py-2 pr-2">Tenant ID</th>
              <th className="py-2 pr-2">Members</th>
            </tr>
          </thead>
          <tbody>
            {teams.length === 0 ? (
              <tr><td colSpan={3} className="py-3 text-muted-foreground">No assignment groups found.</td></tr>
            ) : (
              teams.map((team) => (
                <tr key={team.id} className="border-b border-border/60">
                  <td className="py-2 pr-2 font-medium">{team.name}</td>
                  <td className="py-2 pr-2"><code>{team.tenantId}</code></td>
                  <td className="py-2 pr-2">{team.memberCount}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </section>
  )
}
