import { Link } from '@tanstack/react-router'
import { Clock3, ShieldAlert, Wrench } from 'lucide-react'
import { useState } from 'react'
import type { OwnerAction, OwnerAssetSummary, OwnerDashboardSummary } from '@/api/dashboard.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { MetricInfoTooltip } from '@/components/features/dashboard/MetricInfoTooltip'

// Phase 1 canonical cleanup (Task 15): the dashboard summary schema is
// still on the legacy asset-shaped contract (assetId/assetName/etc). The
// component has been renamed to DeviceOwnerOverview but continues to
// read from the existing schema until Phase 5 rewires the dashboard API
// to the canonical Device identity.

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

function buildActionHeadline(softwareName: string, softwareNames: string[]) {
  if (softwareNames.length === 0) {
    return `Review remediation needed for ${softwareName}`
  }

  return `Review ${summarizeSoftware(softwareNames)} on ${softwareName}`
}

function formatLastSeen(value: string | null | undefined) {
  if (!value) return null
  const d = new Date(value)
  const diff = Date.now() - d.getTime()
  const days = Math.floor(diff / 86_400_000)
  if (days === 0) return 'Seen today'
  if (days === 1) return 'Seen yesterday'
  return `Seen ${days}d ago`
}

function SeverityBar({ critical, high, medium, low }: { critical: number; high: number; medium: number; low: number }) {
  const total = critical + high + medium + low
  if (total === 0) return null
  return (
    <div className="mt-2 flex h-1.5 overflow-hidden rounded-full bg-muted">
      {critical > 0 && <div className="bg-destructive" style={{ width: `${(critical / total) * 100}%` }} />}
      {high > 0 && <div className="bg-orange-500" style={{ width: `${(high / total) * 100}%` }} />}
      {medium > 0 && <div className="bg-amber-500" style={{ width: `${(medium / total) * 100}%` }} />}
      {low > 0 && <div className="bg-muted-foreground/40" style={{ width: `${(low / total) * 100}%` }} />}
    </div>
  )
}

function actionStateTone(value: string) {
  switch (value) {
    case 'InProgress':
      return 'bg-blue-50 text-blue-700 border-blue-200'
    case 'Pending':
      return 'bg-amber-50 text-amber-700 border-amber-200'
    case 'AwaitingDecision':
      return 'bg-orange-50 text-orange-700 border-orange-200'
    default:
      return 'bg-muted text-muted-foreground border-border'
  }
}

function actionStateLabel(value: string) {
  if (value === 'AwaitingDecision') return 'Decision needed'
  return value
}

type ActionSort = 'urgency' | 'severity' | 'team'
type ActionFilter = 'all' | 'overdue'
type DeviceSort = 'risk' | 'name' | 'criticality'

function sortActions(items: OwnerAction[], sort: ActionSort): OwnerAction[] {
  const severityRank = (s: string) => ({ Critical: 4, High: 3, Medium: 2, Low: 1 })[s] ?? 0
  return [...items].sort((a, b) => {
    if (sort === 'urgency') {
      const aOverdue = !!a.dueDate && new Date(a.dueDate) < new Date()
      const bOverdue = !!b.dueDate && new Date(b.dueDate) < new Date()
      if (aOverdue !== bOverdue) return aOverdue ? -1 : 1
      if (a.dueDate && b.dueDate) return new Date(a.dueDate).getTime() - new Date(b.dueDate).getTime()
      if (a.dueDate) return -1
      if (b.dueDate) return 1
      return severityRank(b.severity) - severityRank(a.severity)
    }
    if (sort === 'severity') return severityRank(b.severity) - severityRank(a.severity)
    if (sort === 'team') return a.ownerTeamName.localeCompare(b.ownerTeamName)
    return 0
  })
}

function sortDevices(items: OwnerAssetSummary[], sort: DeviceSort): OwnerAssetSummary[] {
  const critRank = (c: string) => ({ Critical: 4, High: 3, Medium: 2, Low: 1 })[c] ?? 0
  return [...items].sort((a, b) => {
    if (sort === 'name') return a.assetName.localeCompare(b.assetName)
    if (sort === 'criticality') return critRank(b.criticality) - critRank(a.criticality)
    return (b.currentRiskScore ?? 0) - (a.currentRiskScore ?? 0)
  })
}

export function DeviceOwnerOverview({ summary, isLoading }: Props) {
  const [actionSort, setActionSort] = useState<ActionSort>('urgency')
  const [actionFilter, setActionFilter] = useState<ActionFilter>('all')
  const [deviceSort, setDeviceSort] = useState<DeviceSort>('risk')

  const visibleActions = sortActions(
    actionFilter === 'overdue'
      ? summary.actions.filter(a => !!a.dueDate && new Date(a.dueDate) < new Date())
      : summary.actions,
    actionSort
  )

  const visibleDevices = sortDevices(summary.topOwnedAssets, deviceSort)

  return (
    <section className="space-y-6 pb-4">
      <Card className="overflow-hidden rounded-[2rem] border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_12%,var(--background)),var(--card)_55%,var(--background))] shadow-[0_28px_70px_-48px_rgba(0,0,0,0.55)]">
        <CardContent className="p-6 sm:p-8">
          <div className="grid gap-6 xl:grid-cols-[minmax(0,1.5fr)_minmax(20rem,1fr)]">
            <div className="space-y-4">
              <Badge variant="outline" className="rounded-full px-3 py-1 text-[11px] uppercase tracking-[0.18em] text-primary">
                Device owner view
              </Badge>
              <div>
                <h1 className="text-3xl font-semibold tracking-[-0.06em] sm:text-4xl">
                  What needs your attention on the devices you own
                </h1>
                <p className="mt-3 max-w-3xl text-base leading-7 text-muted-foreground">
                  This view focuses only on the devices you are responsible for. It is written to answer three questions quickly: which software on your devices needs attention, what matters most, and what you need to do next.
                </p>
              </div>
            </div>

            <div className="rounded-[1.5rem] border border-border/70 bg-background/45 p-5 backdrop-blur-sm">
              <div className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Your scope</div>
              <div className="mt-3 grid gap-3 sm:grid-cols-2">
                <div className="rounded-[1.2rem] border border-border/60 bg-card/70 px-4 py-4">
                  <div className="flex items-center gap-1.5 text-xs uppercase tracking-[0.18em] text-muted-foreground">
                    Owned devices
                    <MetricInfoTooltip content="Ownership means you are accountable for the device in PatchHound. The count shows the current device scope for that responsibility." />
                  </div>
                  <div className="mt-2 text-3xl font-semibold tracking-[-0.05em]">{summary.ownedAssetCount}</div>
                </div>
                <Link
                  to="/dashboard/my-devices/attention"
                  className="block rounded-[1.2rem] border border-destructive/20 bg-destructive/8 px-4 py-4 transition hover:-translate-y-0.5 hover:shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                >
                  <div className="flex items-center gap-1.5 text-xs uppercase tracking-[0.18em] text-destructive">
                    Need attention
                    <MetricInfoTooltip content="Need attention means the owned device currently carries enough exposure or remediation pressure that it should be reviewed rather than left in routine monitoring." />
                  </div>
                  <div className="mt-2 text-3xl font-semibold tracking-[-0.05em]">{summary.assetsNeedingAttention}</div>
                </Link>
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
              <MetricInfoTooltip content="An open action is remediation work still awaiting follow-through. It tells the owner that something remains unresolved on software running on their devices." />
            </CardDescription>
            <CardTitle className="text-3xl tracking-[-0.05em]">{summary.openActionCount}</CardTitle>
          </CardHeader>
          <CardContent className="pt-0 text-sm text-muted-foreground">
            Software remediation actions already queued for the devices you own.
          </CardContent>
        </Card>
        <Card className={`rounded-[1.5rem] ${summary.overdueActionCount > 0 ? 'border-destructive/30 bg-destructive/5' : 'border-border/70'}`}>
          <CardHeader>
            <CardDescription className="flex items-center gap-2 text-[11px] uppercase tracking-[0.18em]">
              <Clock3 className="size-3.5 text-destructive" />
              Overdue
              <MetricInfoTooltip content="Overdue means the expected action window has passed. These items usually deserve first review because agreed timelines are already slipping." />
            </CardDescription>
            <CardTitle className={`text-3xl tracking-[-0.05em] ${summary.overdueActionCount > 0 ? 'text-destructive' : ''}`}>{summary.overdueActionCount}</CardTitle>
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
            Start with the action list below. It leads with the software and business impact on your device. Technical identifiers are still available, but they are secondary.
          </CardContent>
        </Card>
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.15fr)_minmax(0,1fr)]">
        <Card className="rounded-[1.6rem] border-border/70">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle>Action list</CardTitle>
                <CardDescription className="mt-1">
                  The software on devices you own that needs follow-through.
                </CardDescription>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <div className="flex rounded-lg border border-border/70 text-[11px]">
                  {(['all', 'overdue'] as ActionFilter[]).map(f => (
                    <button
                      key={f}
                      onClick={() => setActionFilter(f)}
                      className={`px-3 py-1.5 capitalize first:rounded-l-lg last:rounded-r-lg transition ${actionFilter === f ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:text-foreground'}`}
                    >
                      {f === 'all' ? 'All' : 'Overdue'}
                    </button>
                  ))}
                </div>
                <div className="flex rounded-lg border border-border/70 text-[11px]">
                  {(['urgency', 'severity', 'team'] as ActionSort[]).map(s => (
                    <button
                      key={s}
                      onClick={() => setActionSort(s)}
                      className={`px-3 py-1.5 capitalize first:rounded-l-lg last:rounded-r-lg transition ${actionSort === s ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:text-foreground'}`}
                    >
                      {s}
                    </button>
                  ))}
                </div>
              </div>
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            {isLoading ? (
              <div className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-10 text-center text-sm text-muted-foreground">
                Loading your action list…
              </div>
            ) : visibleActions.length === 0 ? (
              <div className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-10 text-center text-sm text-muted-foreground">
                {actionFilter === 'overdue'
                  ? 'No overdue actions.'
                  : 'No open software remediation actions are assigned to devices you own right now.'}
              </div>
            ) : (
              visibleActions.map((item) => {
                const isOverdue = !!item.dueDate && new Date(item.dueDate) < new Date()
                return (
                <div key={`${item.tenantSoftwareId}-${item.vulnerabilityId}`} className={`rounded-[1.2rem] border px-4 py-4 ${isOverdue ? 'border-destructive/30 bg-destructive/5' : 'border-border/60 bg-background/35'}`}>
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
                          {actionStateLabel(item.actionState)}
                        </span>
                        {item.episodeRiskBand ? (
                          <span className="text-xs text-muted-foreground">{item.episodeRiskBand} risk</span>
                        ) : null}
                      </div>
                      <div className="mt-2 text-base font-medium tracking-tight">
                        {buildActionHeadline(item.softwareName, item.softwareNames)}
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
                    {item.taskId ? (
                      <Button
                        size="sm"
                        render={
                          <Link
                            to="/remediation/task/$id"
                            params={{ id: item.taskId }}
                          />
                        }
                      >
                        Open remediation task
                      </Button>
                    ) : (
                      <Button
                        size="sm"
                        render={
                          <Link
                            to="/remediation/cases/$caseId"
                            params={{ caseId: item.tenantSoftwareId }}
                          />
                        }
                      >
                        Open remediation case
                      </Button>
                    )}
                    <Button size="sm" variant="outline" render={<Link to="/vulnerabilities/$id" params={{ id: item.vulnerabilityId }} />}>
                      Vulnerability detail
                    </Button>
                  </div>
                </div>
              )
              })
            )}
          </CardContent>
        </Card>

        <Card id="owned-devices-needing-attention" className="rounded-[1.6rem] border-border/70">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle>Devices under the most pressure</CardTitle>
                <CardDescription className="mt-1">
                  Your owned devices with the highest current risk.
                </CardDescription>
              </div>
              <div className="flex rounded-lg border border-border/70 text-[11px]">
                {(['risk', 'name', 'criticality'] as DeviceSort[]).map(s => (
                  <button
                    key={s}
                    onClick={() => setDeviceSort(s)}
                    className={`px-3 py-1.5 capitalize first:rounded-l-lg last:rounded-r-lg transition ${deviceSort === s ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:text-foreground'}`}
                  >
                    {s}
                  </button>
                ))}
              </div>
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            {visibleDevices.map((item) => (
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
                      {formatLastSeen(item.lastSeenAt) ? ` · ${formatLastSeen(item.lastSeenAt)}` : ''}
                    </div>
                    <SeverityBar
                      critical={item.criticalCount ?? 0}
                      high={item.highCount ?? 0}
                      medium={item.mediumCount ?? 0}
                      low={item.lowCount ?? 0}
                    />
                  </div>
                  <div className="text-right">
                    <div className="text-2xl font-semibold tracking-[-0.05em]">
                      {item.currentRiskScore ? Math.round(item.currentRiskScore) : 0}
                    </div>
                  </div>
                </div>
                <div className="mt-3">
                  <Button size="sm" variant="outline" render={<Link to="/devices/$id" params={{ id: item.assetId }} />}>
                    Review device
                  </Button>
                </div>
              </div>
            ))}

            {visibleDevices.length === 0 ? (
              <div className="rounded-[1.2rem] border border-border/60 bg-background/35 px-4 py-10 text-center text-sm text-muted-foreground">
                No owned devices with active pressure were found.
              </div>
            ) : null}
          </CardContent>
        </Card>
      </div>
    </section>
  )
}
