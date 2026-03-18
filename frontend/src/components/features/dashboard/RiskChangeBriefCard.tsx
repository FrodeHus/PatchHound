import { Link } from '@tanstack/react-router'
import { ArrowRight, ArrowUpRight, CheckCircle2 } from 'lucide-react'
import type { DashboardRiskChangeBrief } from '@/api/dashboard.schemas'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { InsetPanel } from '@/components/ui/inset-panel'

type RiskChangeBriefCardProps = {
  brief: DashboardRiskChangeBrief
  isLoading?: boolean
}

export function RiskChangeBriefCard({ brief, isLoading }: RiskChangeBriefCardProps) {
  return (
    <Card className="overflow-hidden rounded-2xl border-border/70 bg-card/92 shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
      <CardHeader className="p-5 pb-2">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="text-xs uppercase tracking-[0.24em] text-muted-foreground">Change brief</p>
            <CardTitle className="mt-2 text-xl font-semibold tracking-tight">Risk change brief</CardTitle>
          </div>
          <Badge variant="outline" className="rounded-full border-border/70 bg-muted text-foreground">
            Last 24 hours
          </Badge>
        </div>
        {brief.aiSummary ? (
          <p className="mt-3 max-w-3xl text-sm text-muted-foreground">{brief.aiSummary}</p>
        ) : null}
      </CardHeader>
      <CardContent className="p-5 pt-2">
        {isLoading ? (
          <div className="h-32 animate-pulse rounded-2xl bg-muted/60" />
        ) : (
        <>
          <div className="grid gap-4 lg:grid-cols-2">
            <RiskChangeLane
              title="New"
              count={brief.appearedCount}
              tone="new"
              emptyText="No new high or critical vulnerabilities in the last 24 hours."
              items={brief.appeared}
            />
            <RiskChangeLane
              title="Resolved"
              count={brief.resolvedCount}
              tone="resolved"
              emptyText="No high or critical vulnerabilities resolved in the last 24 hours."
              items={brief.resolved}
            />
          </div>

          <div className="mt-4 flex justify-end">
            <Link
              to="/vulnerabilities/changes"
              className="inline-flex items-center gap-2 text-sm font-medium text-primary hover:underline"
            >
              Open full change log
              <ArrowRight className="size-4" />
            </Link>
          </div>
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
      ? 'border-amber-300/30 bg-amber-500/10 text-amber-200'
      : 'border-emerald-300/30 bg-emerald-500/10 text-emerald-200'

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
              key={item.tenantVulnerabilityId}
              to="/vulnerabilities/$id"
              params={{ id: item.tenantVulnerabilityId }}
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
                        : 'rounded-full border-amber-300/30 bg-amber-500/10 text-amber-200'
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
                <ArrowUpRight className="mt-1 size-4 shrink-0 text-amber-200" />
              ) : (
                <CheckCircle2 className="mt-1 size-4 shrink-0 text-emerald-200" />
              )}
            </Link>
          ))
        )}
      </div>
    </InsetPanel>
  )
}
