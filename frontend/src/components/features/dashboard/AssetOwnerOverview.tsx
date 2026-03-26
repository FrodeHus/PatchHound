import { Link } from '@tanstack/react-router'
import { AlertTriangle, Clock3, ShieldAlert, Wrench } from 'lucide-react'
import type { OwnerDashboardSummary } from '@/api/dashboard.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { MetricInfoTooltip } from '@/components/features/dashboard/MetricInfoTooltip'

type Props = {
  summary: OwnerDashboardSummary
  isLoading: boolean
}

function formatDueDate(value: string | null) {
  if (!value) return 'No due date'
  return new Date(value).toLocaleDateString('en-GB', { day: '2-digit', month: 'short' })
}

function summarizeSoftware(names: string[]) {
  if (names.length === 0) return 'Affected software needs review'
  if (names.length === 1) return names[0]
  if (names.length === 2) return `${names[0]} and ${names[1]}`
  return `${names[0]}, ${names[1]}, and ${names.length - 2} more`
}

function actionStateTone(value: string) {
  switch (value) {
    case 'InProgress':
      return 'bg-blue-50 text-blue-700 border-blue-200'
    case 'Pending':
      return 'bg-amber-50 text-amber-700 border-amber-200'
    default:
      return 'bg-muted text-muted-foreground border-border'
  }
}

export function AssetOwnerOverview({ summary, isLoading }: Props) {
  return (
    <section className="space-y-6 pb-4">
      <Card className="overflow-hidden rounded-[2rem] border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_12%,var(--background)),var(--card)_55%,var(--background))] shadow-[0_28px_70px_-48px_rgba(0,0,0,0.55)]">
        <CardContent className="p-6 sm:p-8">
          <div className="grid gap-6 xl:grid-cols-[minmax(0,1.5fr)_minmax(20rem,1fr)]">
            <div className="space-y-4">
              <Badge variant="outline" className="rounded-full px-3 py-1 text-[11px] uppercase tracking-[0.18em] text-primary">
                Asset owner view
              </Badge>
              <div>
                <h1 className="text-3xl font-semibold tracking-[-0.06em] sm:text-4xl">
                  What needs your attention on the assets you own
                </h1>
                <p className="mt-3 max-w-3xl text-base leading-7 text-muted-foreground">
                  This view focuses only on the assets you are responsible for. It is written to answer three questions quickly: which software on your assets needs attention, what matters most, and what you need to do next.
                </p>
              </div>
            </div>

            <div className="rounded-[1.5rem] border border-border/70 bg-background/45 p-5 backdrop-blur-sm">
              <div className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Your scope</div>
              <div className="mt-3 grid gap-3 sm:grid-cols-2">
                <div className="rounded-[1.2rem] border border-border/60 bg-card/70 px-4 py-4">
                  <div className="flex items-center gap-1.5 text-xs uppercase tracking-[0.18em] text-muted-foreground">
                    Owned assets
                    <MetricInfoTooltip content="Ownership means you are accountable for the asset in PatchHound. The count shows the current asset scope for that responsibility." />
                  </div>
                  <div className="mt-2 text-3xl font-semibold tracking-[-0.05em]">{summary.ownedAssetCount}</div>
                </div>
                <div className="rounded-[1.2rem] border border-destructive/20 bg-destructive/8 px-4 py-4">
                  <div className="flex items-center gap-1.5 text-xs uppercase tracking-[0.18em] text-destructive">
                    Need attention
                    <MetricInfoTooltip content="Need attention means the owned asset currently carries enough exposure or remediation pressure that it should be reviewed rather than left in routine monitoring." />
                  </div>
                  <div className="mt-2 text-3xl font-semibold tracking-[-0.05em]">{summary.assetsNeedingAttention}</div>
                </div>
              </div>
            </div>
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-4 xl:grid-cols-3">
        <Card className="rounded-[1.5rem] border-border/70">
          <CardHeader>
            <CardDescription className="flex items-center gap-2 text-[11px] uppercase tracking-[0.18em]">
              <ShieldAlert className="size-3.5 text-primary" />
              Open actions
              <MetricInfoTooltip content="An open action is remediation work still awaiting follow-through. It tells the owner that something remains unresolved on software running on their assets." />
            </CardDescription>
            <CardTitle className="text-3xl tracking-[-0.05em]">{summary.openActionCount}</CardTitle>
          </CardHeader>
          <CardContent className="pt-0 text-sm text-muted-foreground">
            Software remediation actions already queued for the assets you own.
          </CardContent>
        </Card>
        <Card className="rounded-[1.5rem] border-border/70">
          <CardHeader>
            <CardDescription className="flex items-center gap-2 text-[11px] uppercase tracking-[0.18em]">
              <Clock3 className="size-3.5 text-destructive" />
              Overdue
              <MetricInfoTooltip content="Overdue means the expected action window has passed. These items usually deserve first review because agreed timelines are already slipping." />
            </CardDescription>
            <CardTitle className="text-3xl tracking-[-0.05em]">{summary.overdueActionCount}</CardTitle>
          </CardHeader>
          <CardContent className="pt-0 text-sm text-muted-foreground">
            Items that have passed their due date and should be reviewed first.
          </CardContent>
        </Card>
        <Card className="rounded-[1.5rem] border-border/70">
          <CardHeader>
            <CardDescription className="flex items-center gap-2 text-[11px] uppercase tracking-[0.18em]">
              <Wrench className="size-3.5 text-chart-3" />
              Plain-language focus
            </CardDescription>
          </CardHeader>
          <CardContent className="pt-0 text-sm leading-6 text-muted-foreground">
            Start with the action list below. It leads with the software and business impact on your asset. Technical identifiers are still available, but they are secondary.
          </CardContent>
        </Card>
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.15fr)_minmax(0,1fr)]">
        <Card className="rounded-[1.6rem] border-border/70">
          <CardHeader>
            <CardTitle>Action list</CardTitle>
            <CardDescription>
              The software on assets you own that needs follow-through, ordered by urgency.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {isLoading ? (
              <div className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-10 text-center text-sm text-muted-foreground">
                Loading your action list…
              </div>
            ) : summary.actions.length === 0 ? (
              <div className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-10 text-center text-sm text-muted-foreground">
                No open software remediation actions are assigned to assets you own right now.
              </div>
            ) : (
              summary.actions.map((item) => (
                <div key={`${item.tenantSoftwareId}-${item.tenantVulnerabilityId}`} className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-4">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <Badge variant="outline" className="rounded-full px-2 py-0.5 text-[11px]">
                          {item.severity}
                        </Badge>
                        <Badge variant="outline" className="rounded-full px-2 py-0.5 text-[11px]">
                          {item.ownerTeamName}
                        </Badge>
                        <span className={`rounded-full border px-2 py-0.5 text-[11px] font-medium ${actionStateTone(item.actionState)}`}>
                          {item.actionState}
                        </span>
                        {item.episodeRiskBand ? (
                          <span className="text-xs text-muted-foreground">{item.episodeRiskBand} risk</span>
                        ) : null}
                      </div>
                      <div className="mt-2 text-base font-medium tracking-tight">
                        Review {summarizeSoftware(item.softwareNames)} for {item.softwareName}
                      </div>
                      <div className="mt-1 text-sm text-muted-foreground">
                        {item.ownerSummary}
                      </div>
                      {item.softwareNames.length > 0 ? (
                        <div className="mt-2 text-sm text-muted-foreground">
                          Software covered by this remediation: {item.softwareNames.join(', ')}
                        </div>
                      ) : null}
                      <div className="mt-2 text-xs text-muted-foreground">
                        Technical reference: {item.externalId}
                      </div>
                    </div>
                    <div className="text-right text-sm text-muted-foreground">
                      <div>Due {formatDueDate(item.dueDate)}</div>
                      <div className="mt-1">Assigned to {item.ownerTeamName}</div>
                    </div>
                  </div>
                  <div className="mt-3 flex gap-2">
                    <Button
                      size="sm"
                      render={
                        <Link
                          to="/remediation/task/$id"
                          params={{ id: item.taskId ?? item.tenantSoftwareId }}
                        />
                      }
                      disabled={!item.taskId}
                    >
                      Open remediation task
                    </Button>
                    <Button size="sm" variant="outline" render={<Link to="/vulnerabilities/$id" params={{ id: item.tenantVulnerabilityId }} />}>
                      Vulnerability detail
                    </Button>
                  </div>
                </div>
              ))
            )}
          </CardContent>
        </Card>

        <Card className="rounded-[1.6rem] border-border/70">
          <CardHeader>
            <CardTitle>Assets under the most pressure</CardTitle>
            <CardDescription>
              Your owned assets with the highest current risk.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {summary.topOwnedAssets.map((item) => (
              <div key={item.assetId} className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-4">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="font-medium tracking-tight">{item.assetName}</div>
                    <div className="mt-2 flex flex-wrap items-center gap-2">
                      <Badge variant="outline" className="rounded-full px-2 py-0.5 text-[11px]">
                        {item.criticality}
                      </Badge>
                      <Badge variant="outline" className="rounded-full px-2 py-0.5 text-[11px]">
                        {item.deviceGroupName || 'Ungrouped'}
                      </Badge>
                      {item.riskBand ? (
                        <Badge variant="outline" className="rounded-full px-2 py-0.5 text-[11px]">
                          {item.riskBand}
                        </Badge>
                      ) : null}
                    </div>
                    <div className="mt-2 text-sm text-muted-foreground">
                      {item.openEpisodeCount} open exposure items
                    </div>
                  </div>
                  <div className="text-right">
                    <div className="text-2xl font-semibold tracking-[-0.05em]">
                      {item.currentRiskScore ? Math.round(item.currentRiskScore) : 0}
                    </div>
                  </div>
                </div>
                <div className="mt-3">
                  <Button size="sm" variant="outline" render={<Link to="/assets/$id" params={{ id: item.assetId }} />}>
                    Review asset
                  </Button>
                </div>
              </div>
            ))}

            {summary.topOwnedAssets.length === 0 ? (
              <div className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-10 text-center text-sm text-muted-foreground">
                No owned assets with active pressure were found.
              </div>
            ) : null}
          </CardContent>
        </Card>
      </div>
    </section>
  )
}
