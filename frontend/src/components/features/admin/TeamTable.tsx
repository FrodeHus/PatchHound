import type { TeamItem } from '@/api/teams.schemas'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { PaginationControls } from '@/components/ui/pagination-controls'

type TeamTableProps = {
  teams: TeamItem[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  selectedTeamId: string | null
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
  onSelectTeam: (teamId: string) => void
}

export function TeamTable({
  teams,
  totalCount,
  page,
  pageSize,
  totalPages,
  selectedTeamId,
  onPageChange,
  onPageSizeChange,
  onSelectTeam,
}: TeamTableProps) {
  return (
    <Card className="rounded-[28px] border-border/70 bg-card/82">
      <CardHeader>
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <CardTitle>Assignment Group Directory</CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              Pick an assignment group to open its asset-assignment workspace.
            </p>
          </div>
          <Badge variant="outline" className="rounded-full border-border/70 bg-background/60">
            {totalCount} total
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {teams.length === 0 ? (
          <div className="rounded-2xl border border-border/60 bg-background/30 px-4 py-6 text-sm text-muted-foreground">
            No assignment groups found.
          </div>
        ) : (
          teams.map((team) => {
            const isSelected = selectedTeamId === team.id

            return (
              <button
                key={team.id}
                type="button"
                onClick={() => onSelectTeam(team.id)}
                className={[
                  'rounded-[24px] border p-4 text-left transition',
                  isSelected
                    ? 'border-primary/30 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_68%),var(--color-card)]'
                    : 'border-border/70 bg-background/30 hover:border-primary/20 hover:bg-background/45',
                ].join(' ')}
              >
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="text-base font-semibold tracking-tight">{team.name}</p>
                    <p className="mt-1 text-sm text-muted-foreground">{team.tenantName}</p>
                  </div>
                  {isSelected ? (
                    <Badge className="rounded-full border border-primary/20 bg-primary/10 text-primary hover:bg-primary/10">
                      Active
                    </Badge>
                  ) : null}
                </div>
                <div className="mt-4 flex items-center justify-between rounded-2xl border border-border/60 bg-card/40 px-3 py-2">
                  <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Members</span>
                  <span className="text-sm font-medium">{team.memberCount}</span>
                </div>
              </button>
            )
          })
        )}
      </CardContent>
      <div className="px-6 pb-6">
        <PaginationControls
          page={page}
          pageSize={pageSize}
          totalCount={totalCount}
          totalPages={totalPages}
          onPageChange={onPageChange}
          onPageSizeChange={onPageSizeChange}
        />
      </div>
    </Card>
  )
}
