import { Skeleton } from '@/components/ui/skeleton'
import { useState } from 'react'
import { Link } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { ArrowRight, ArrowUpRight, CheckCircle2 } from 'lucide-react'
import type { DashboardRiskChangeBrief } from '@/api/dashboard.schemas'
import { fetchDashboardRiskChanges } from '@/api/dashboard.functions'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { toneBadge, toneText } from '@/lib/tone-classes'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { InsetPanel } from '@/components/ui/inset-panel'

type RiskChangeBriefCardProps = {
  brief: DashboardRiskChangeBrief
  isLoading?: boolean
}

type TimeWindow = '24h' | '7d'

export function RiskChangeBriefCard({ brief, isLoading }: RiskChangeBriefCardProps) {
  const [window, setWindow] = useState<TimeWindow>('24h')

  const extendedQuery = useQuery({
    queryKey: ['dashboard', 'risk-changes', 7],
    queryFn: () => fetchDashboardRiskChanges({ data: { days: 7 } }),
    staleTime: 30_000,
    enabled: window === '7d',
  })

  const activeBrief = window === '7d' && extendedQuery.data ? extendedQuery.data : brief
  const activeLoading = window === '7d' ? extendedQuery.isFetching : isLoading
  const windowLabel = window === '24h' ? 'Last 24 hours' : 'Last 7 days'

  return (
    <Card className="overflow-hidden rounded-2xl border-border/70 bg-card/92 shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
      <CardHeader className="p-5 pb-2">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Change brief</p>
            <CardTitle className="mt-2 text-xl font-semibold tracking-tight">Risk change brief</CardTitle>
          </div>
          <div className="flex items-center gap-2">
            <div className="flex rounded-full border border-border/70 bg-muted/50 p-0.5">
              <Button
                type="button"
                variant="ghost"
                className={`h-7 rounded-full px-3 text-xs ${window === '24h' ? 'bg-background shadow-sm' : 'text-muted-foreground'}`}
                onClick={() => setWindow('24h')}
              >
                24h
              </Button>
              <Button
                type="button"
                variant="ghost"
                className={`h-7 rounded-full px-3 text-xs ${window === '7d' ? 'bg-background shadow-sm' : 'text-muted-foreground'}`}
                onClick={() => setWindow('7d')}
              >
                7d
              </Button>
            </div>
            <Badge variant="outline" className="rounded-full border-border/70 bg-muted text-foreground">
              {windowLabel}
            </Badge>
          </div>
        </div>
        {activeBrief.aiSummary ? (
          <p className="mt-3 max-w-3xl text-sm text-muted-foreground">{activeBrief.aiSummary}</p>
        ) : null}
      </CardHeader>
      <CardContent className="p-5 pt-2">
        {activeLoading ? (
          <Skeleton className="h-32 " />
        ) : (
        <>
          <div className="grid gap-4 lg:grid-cols-2">
            <RiskChangeLane
              title="New"
              count={activeBrief.appearedCount}
              tone="new"
              emptyText={`No new high or critical vulnerabilities in the ${windowLabel.toLowerCase()}.`}
              items={activeBrief.appeared}
            />
            <RiskChangeLane
              title="Resolved"
              count={activeBrief.resolvedCount}
              tone="resolved"
              emptyText={`No high or critical vulnerabilities resolved in the ${windowLabel.toLowerCase()}.`}
              items={activeBrief.resolved}
            />
          </div>

          {activeBrief.appearedCount > 0 || activeBrief.resolvedCount > 0 ? (
            <InsetPanel className="mt-4 flex items-center justify-between px-4 py-3">
              <div className="flex items-center gap-3 text-sm">
                <span className="text-muted-foreground">Net delta:</span>
                <span className={`font-semibold tabular-nums ${activeBrief.appearedCount > activeBrief.resolvedCount ? 'text-tone-danger-foreground' : 'text-tone-success-foreground'}`}>
                  {activeBrief.appearedCount > activeBrief.resolvedCount ? '+' : ''}
                  {activeBrief.appearedCount - activeBrief.resolvedCount}
                </span>
              </div>
              <Link
                to="/vulnerabilities/changes"
                className="inline-flex items-center gap-2 text-sm font-medium text-primary hover:underline"
              >
                Open full change log
                <ArrowRight className="size-4" />
              </Link>
            </InsetPanel>
          ) : (
            <div className="mt-4 flex justify-end">
              <Link
                to="/vulnerabilities/changes"
                className="inline-flex items-center gap-2 text-sm font-medium text-primary hover:underline"
              >
                Open full change log
                <ArrowRight className="size-4" />
              </Link>
            </div>
          )}
        </>
        )}
      </CardContent>
    </Card>
  )
}

type RiskChangeLaneProps = {
  title: string
  count: number
  tone: 'new' | 'resolved'
  emptyText: string
  items: DashboardRiskChangeBrief['appeared']
}

function RiskChangeLane({ title, count, tone, emptyText, items }: RiskChangeLaneProps) {
  const countClassName =
    tone === 'new'
      ? toneBadge('warning')
      : toneBadge('success')

  return (
    <InsetPanel className="p-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{title}</p>
          <p className="mt-2 text-3xl font-semibold tracking-[-0.04em]">{count}</p>
        </div>
        <Badge className={`rounded-full border ${countClassName} hover:bg-transparent`}>
          {title === 'New' ? 'Appeared' : 'Resolved'}
        </Badge>
      </div>

      <div className="mt-4 space-y-2">
        {items.length === 0 ? (
          <p className="text-sm text-muted-foreground">{emptyText}</p>
        ) : (
          items.map((item) => (
            <Link
              key={item.vulnerabilityId}
              to="/vulnerabilities/$id"
              params={{ id: item.vulnerabilityId }}
              className="flex items-start justify-between gap-3 rounded-xl border border-border/60 bg-background px-3 py-3 transition-colors hover:bg-accent/20"
            >
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <span className="text-sm font-medium">{item.externalId}</span>
                  <Badge
                    variant="outline"
                    className={
                      item.severity === 'Critical'
                        ? 'rounded-full border-destructive/30 bg-destructive/10 text-destructive'
                        : `rounded-full ${toneBadge('warning')}`
                    }
                  >
                    {item.severity}
                  </Badge>
                </div>
                <p className="mt-1 truncate text-sm text-muted-foreground">{item.title}</p>
                <p className="mt-1 text-xs text-muted-foreground">
                  {item.affectedAssetCount} {item.affectedAssetCount === 1 ? 'asset' : 'assets'}
                </p>
              </div>
              {tone === 'new' ? (
                <ArrowUpRight className={`mt-1 size-4 shrink-0 ${toneText('warning')}`} />
              ) : (
                <CheckCircle2 className={`mt-1 size-4 shrink-0 ${toneText('success')}`} />
              )}
            </Link>
          ))
        )}
      </div>
    </InsetPanel>
  )
}
