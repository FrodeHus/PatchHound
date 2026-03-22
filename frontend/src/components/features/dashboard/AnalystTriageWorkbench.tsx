import { Link } from '@tanstack/react-router'
import { ShieldAlert, Siren, TimerReset, Zap } from 'lucide-react'
import type { DashboardSummary, UnhandledVulnerability } from '@/api/dashboard.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

type Props = {
  items: UnhandledVulnerability[]
  summary: DashboardSummary
  isLoading?: boolean
}

function severityTone(severity: string) {
  if (severity === 'Critical') return 'border-destructive/25 bg-destructive/10 text-destructive'
  if (severity === 'High') return 'border-primary/25 bg-primary/10 text-primary'
  if (severity === 'Medium') return 'border-chart-4/25 bg-chart-4/10 text-chart-4'
  return 'border-border bg-muted/50 text-muted-foreground'
}

function formatRelativeTime(value: string) {
  const date = new Date(value)
  const diffHours = Math.max(0, Math.round((Date.now() - date.getTime()) / 36e5))

  if (diffHours < 24) {
    return `${diffHours}h ago`
  }

  return `${Math.round(diffHours / 24)}d ago`
}

export function AnalystTriageWorkbench({ items, summary, isLoading }: Props) {
  const criticalUnhandled = items.filter((item) => item.severity === 'Critical').length
  const highUnhandled = items.filter((item) => item.severity === 'High').length

  return (
    <Card className="rounded-[2rem] border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_94%,black),var(--card))] shadow-[inset_0_1px_0_rgba(255,255,255,0.04)]">
      <CardHeader className="gap-4 p-6 pb-3">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Analyst Workbench</p>
            <CardTitle className="mt-2 text-2xl font-semibold tracking-tight">Latest unhandled vulnerabilities</CardTitle>
            <CardDescription className="mt-2 max-w-3xl leading-6">
              Open vulnerabilities with no active remediation task and no approved risk acceptance. Ordered for triage: severity first, then newest unresolved appearance.
            </CardDescription>
          </div>
          <Button
            variant="outline"
            render={<Link to="/vulnerabilities" search={{ page: 1, pageSize: 25, search: '', severity: '', status: '', source: '', presentOnly: true, recurrenceOnly: false, minAgeDays: '', publicExploitOnly: false, knownExploitedOnly: false, activeAlertOnly: false }} />}
          >
            Open vulnerability workbench
          </Button>
        </div>

        <div className="grid gap-3 sm:grid-cols-3">
          <div className="rounded-[1.25rem] border border-destructive/20 bg-destructive/8 px-4 py-3">
            <div className="flex items-center gap-2 text-xs uppercase tracking-[0.18em] text-destructive">
              <Siren className="size-3.5" />
              Critical unhandled
            </div>
            <div className="mt-2 text-3xl font-semibold tracking-[-0.05em]">{criticalUnhandled}</div>
          </div>
          <div className="rounded-[1.25rem] border border-primary/20 bg-primary/8 px-4 py-3">
            <div className="flex items-center gap-2 text-xs uppercase tracking-[0.18em] text-primary">
              <ShieldAlert className="size-3.5" />
              High unhandled
            </div>
            <div className="mt-2 text-3xl font-semibold tracking-[-0.05em]">{highUnhandled}</div>
          </div>
          <div className="rounded-[1.25rem] border border-border/70 bg-background/35 px-4 py-3">
            <div className="flex items-center gap-2 text-xs uppercase tracking-[0.18em] text-muted-foreground">
              <TimerReset className="size-3.5" />
              Overdue pressure
            </div>
            <div className="mt-2 text-3xl font-semibold tracking-[-0.05em]">{summary.overdueTaskCount}</div>
          </div>
        </div>
      </CardHeader>

      <CardContent className="p-6 pt-2">
        <div className="space-y-3">
          {isLoading ? (
            <div className="rounded-[1.25rem] border border-border/60 bg-background/25 px-4 py-12 text-center text-sm text-muted-foreground">
              Loading analyst queue…
            </div>
          ) : items.length === 0 ? (
            <div className="rounded-[1.25rem] border border-border/60 bg-background/25 px-4 py-12 text-center">
              <div className="text-lg font-medium tracking-tight">No unhandled vulnerabilities are waiting</div>
              <p className="mt-2 text-sm text-muted-foreground">
                Open vulnerabilities are either already in remediation flow or covered by approved acceptance.
              </p>
            </div>
          ) : (
            items.map((item) => (
              <div
                key={item.id}
                className="grid gap-4 rounded-[1.35rem] border border-border/60 bg-background/35 px-4 py-4 lg:grid-cols-[minmax(0,1.7fr)_auto_auto_auto]"
              >
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge variant="outline" className={`rounded-full ${severityTone(item.severity)}`}>
                      {item.severity}
                    </Badge>
                    <span className="text-sm font-medium">{item.externalId}</span>
                    {item.cvssScore != null ? (
                      <span className="text-xs text-muted-foreground">CVSS {item.cvssScore.toFixed(1)}</span>
                    ) : null}
                  </div>
                  <div className="mt-2 text-base font-medium tracking-tight">{item.title}</div>
                  <div className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-sm text-muted-foreground">
                    <span>{item.affectedAssetCount} affected assets</span>
                    <span>Published {item.daysSincePublished}d ago</span>
                    <span>Last seen {formatRelativeTime(item.latestSeenAt)}</span>
                  </div>
                </div>

                <div className="flex items-center">
                  <div className="rounded-full border border-border/60 px-3 py-1.5 text-xs uppercase tracking-[0.18em] text-muted-foreground">
                    Unhandled
                  </div>
                </div>

                <div className="flex items-center">
                  <div className="flex items-center gap-2 text-sm text-muted-foreground">
                    <Zap className="size-4 text-primary" />
                    Needs triage
                  </div>
                </div>

                <div className="flex items-center justify-start lg:justify-end">
                  <Button
                    size="sm"
                    render={<Link to="/vulnerabilities/$id" params={{ id: item.id }} />}
                  >
                    Review
                  </Button>
                </div>
              </div>
            ))
          )}
        </div>
      </CardContent>
    </Card>
  )
}
