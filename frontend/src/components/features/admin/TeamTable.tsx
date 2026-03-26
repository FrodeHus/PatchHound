import { useMemo } from 'react'
import type { TeamItem } from '@/api/teams.schemas'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { PaginationControls } from '@/components/ui/pagination-controls'
import { cn } from '@/lib/utils'

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
  const summary = useMemo(() => {
    const dynamicGroups = teams.filter((team) => team.isDynamic).length
    const fallbackGroups = teams.filter((team) => team.isDefault).length

    return {
      dynamicGroups,
      fallbackGroups,
    }
  }, [selectedTeamId, teams])

  return (
    <Card className="h-full rounded-[2rem] border-border/70 bg-card/80 shadow-sm">
      <CardHeader className="space-y-4 pb-3">
        <div>
          <p className="text-xs uppercase tracking-[0.22em] text-muted-foreground">Assignment group directory</p>
          <CardTitle className="mt-2 text-xl font-semibold tracking-[-0.04em]">Pick a group to open its workspace</CardTitle>
          <p className="mt-2 text-sm text-muted-foreground">
            Use the directory as navigation. The selected group opens on the right with members, rules, and asset assignment.
          </p>
        </div>
        <div className="grid grid-cols-3 gap-2">
          <DirectoryMetric label="Groups" value={String(totalCount)} tone="accent" />
          <DirectoryMetric label="Dynamic" value={String(summary.dynamicGroups)} />
          <DirectoryMetric label="Fallback" value={String(summary.fallbackGroups)} />
        </div>
      </CardHeader>
      <CardContent className="flex h-[calc(100%-10.5rem)] min-h-[28rem] flex-col gap-4">
        {teams.length === 0 ? (
          <div className="flex flex-1 items-center justify-center rounded-[1.5rem] border border-dashed border-border/70 bg-muted/10 px-6 text-center text-sm text-muted-foreground">
            No assignment groups found. Create one to start assigning ownership.
          </div>
        ) : (
          <div className="flex-1 space-y-2 overflow-y-auto pr-1">
            {teams.map((team) => {
              const isSelected = selectedTeamId === team.id
              return (
                <button
                  key={team.id}
                  type="button"
                  onClick={() => onSelectTeam(team.id)}
                  className={cn(
                    'w-full rounded-[1.5rem] border px-4 py-4 text-left transition',
                    isSelected
                      ? 'border-primary/35 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] shadow-sm'
                      : 'border-border/70 bg-background/45 hover:border-primary/20 hover:bg-background/70',
                  )}
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="truncate text-sm font-semibold tracking-tight">{team.name}</span>
                        {team.isDefault ? (
                          <Badge className="rounded-full border border-amber-300/60 bg-amber-500/10 text-amber-700 hover:bg-amber-500/10 dark:text-amber-300">
                            Fallback
                          </Badge>
                        ) : null}
                        {team.isDynamic ? (
                          <Badge className="rounded-full border border-primary/20 bg-primary/10 text-primary hover:bg-primary/10">
                            Dynamic
                          </Badge>
                        ) : (
                          <Badge variant="outline" className="rounded-full border-border/70 bg-background/50 text-muted-foreground">
                            Static
                          </Badge>
                        )}
                      </div>
                      <p className="mt-1 text-xs text-muted-foreground">{team.tenantName}</p>
                    </div>
                    {isSelected ? (
                      <Badge className="rounded-full border border-primary/20 bg-primary/10 text-primary hover:bg-primary/10">
                        Open
                      </Badge>
                    ) : null}
                  </div>
                  <div className="mt-4 grid grid-cols-3 gap-2 text-left">
                    <ListMetric label="Members" value={String(team.memberCount)} />
                    <ListMetric label="Risk" value={typeof team.currentRiskScore === 'number' ? team.currentRiskScore.toFixed(0) : '0'} />
                    <ListMetric label="Mode" value={team.isDynamic ? 'Rule' : 'Manual'} />
                  </div>
                </button>
              )
            })}
          </div>
        )}
        <PaginationControls
          page={page}
          pageSize={pageSize}
          totalCount={totalCount}
          totalPages={totalPages}
          onPageChange={onPageChange}
          onPageSizeChange={onPageSizeChange}
        />
      </CardContent>
    </Card>
  )
}

function DirectoryMetric({
  label,
  value,
  tone = 'default',
}: {
  label: string
  value: string
  tone?: 'default' | 'accent'
}) {
  return (
    <div
      className={cn(
        'rounded-2xl border px-3 py-3',
        tone === 'accent'
          ? 'border-primary/20 bg-primary/10'
          : 'border-border/70 bg-muted/15',
      )}
    >
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
      <p className="mt-2 text-lg font-semibold tracking-tight">{value}</p>
    </div>
  )
}

function ListMetric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-border/60 bg-background/60 px-3 py-2">
      <p className="text-[10px] uppercase tracking-[0.16em] text-muted-foreground">{label}</p>
      <p className="mt-1 text-sm font-medium">{value}</p>
    </div>
  )
}
