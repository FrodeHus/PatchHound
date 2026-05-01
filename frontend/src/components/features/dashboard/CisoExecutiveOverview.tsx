import { ArrowDownRight, ArrowUpRight, Building2, Clock3, Flame, RefreshCw, Siren, Trophy } from 'lucide-react'
import { useQuery } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import type { DashboardSummary, TrendData } from '@/api/dashboard.schemas'
import { fetchRiskScoreSummary } from '@/api/risk-score.functions'
import type { RiskScoreSummary } from '@/api/risk-score.schemas'
import { MetricInfoTooltip } from '@/components/features/dashboard/MetricInfoTooltip'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { cn } from '@/lib/utils'
import type { LucideIcon } from 'lucide-react'

type Filters = {
  minAgeDays?: number
  platform?: string
  deviceGroup?: string
}

type Props = {
  summary: DashboardSummary
  trends: TrendData
  isLoading: boolean
  filters: Filters
}

type ExecutiveTone = 'contained' | 'watch' | 'critical'
type RiskGaugeBand = 'contained' | 'elevated' | 'high' | 'critical'

function formatPercent(value: number) {
  return `${Math.round(value)}%`
}

function formatDays(value: number) {
  if (!Number.isFinite(value) || value <= 0) {
    return '0d'
  }

  return `${Math.round(value)}d`
}

function describePressureArea(group: DashboardSummary['vulnerabilitiesByDeviceGroup'][number] | undefined) {
  if (!group) {
    return 'No active concentration by device group.'
  }

  const highPriorityCount = group.critical + group.high
  const mediumCount = group.medium
  const assetCount = group.assetCount ?? 0
  const openEpisodeCount = group.openEpisodeCount ?? 0

  if (highPriorityCount > 0) {
    return `${highPriorityCount} high-priority exposures across ${assetCount} assets.`
  }

  if (mediumCount > 0) {
    return `${mediumCount} medium-pressure exposures across ${assetCount} assets, with a current risk score of ${Math.round(group.currentRiskScore ?? 0)}.`
  }

  if (openEpisodeCount > 0) {
    return `${openEpisodeCount} active exposure items across ${assetCount} assets, even though none are currently in the top severity bands.`
  }

  return `Current risk score ${Math.round(group.currentRiskScore ?? 0)} across ${assetCount} assets.`
}

function deriveTone(summary: DashboardSummary): {
  tone: ExecutiveTone
  headline: string
  narrative: string
} {
  const critical = summary.vulnerabilitiesBySeverity.Critical ?? 0
  const overdue = summary.overdueTaskCount
  const recurrence = summary.recurrenceRatePercent

  if (critical >= 25 || overdue >= 40 || recurrence >= 20) {
    return {
      tone: 'critical',
      headline: 'Exposure needs leadership attention',
      narrative: 'Critical exposure, overdue remediation, or repeat weakness is high enough to require active management follow-through.',
    }
  }

  if (critical >= 8 || overdue >= 15 || recurrence >= 8) {
    return {
      tone: 'watch',
      headline: 'Exposure is elevated but controllable',
      narrative: 'The organization is carrying meaningful pressure, but the backlog still looks containable if remediation pace holds.',
    }
  }

  return {
    tone: 'contained',
    headline: 'Exposure is currently contained',
    narrative: 'No major concentration of critical pressure stands out right now. Focus should stay on sustaining pace and preventing repeat drift.',
  }
}

function getTrendDirection(trends: TrendData) {
  const sorted = [...trends.items].sort((a, b) => a.date.localeCompare(b.date))
  const window = sorted.slice(-14)
  if (window.length < 2) {
    return { delta: 0, direction: 'flat' as const }
  }

  const aggregate = new Map<string, number>()
  for (const item of window) {
    aggregate.set(item.date, (aggregate.get(item.date) ?? 0) + item.count)
  }

  const dates = [...aggregate.keys()].sort()
  const first = aggregate.get(dates[0]) ?? 0
  const last = aggregate.get(dates.at(-1) ?? '') ?? 0
  const delta = last - first

  return {
    delta,
    direction: delta > 0 ? 'up' as const : delta < 0 ? 'down' as const : 'flat' as const,
  }
}

function riskGaugeBand(score: number): RiskGaugeBand {
  if (score >= 900) return 'critical'
  if (score >= 750) return 'high'
  if (score >= 500) return 'elevated'
  return 'contained'
}

function riskGaugeLabel(score: number) {
  switch (riskGaugeBand(score)) {
    case 'critical':
      return 'Critical'
    case 'high':
      return 'High'
    case 'elevated':
      return 'Elevated'
    default:
      return 'Contained'
  }
}

function riskGaugeTone(score: number) {
  switch (riskGaugeBand(score)) {
    case 'critical':
      return 'text-destructive'
    case 'high':
      return 'text-tone-warning-foreground'
    case 'elevated':
      return 'text-chart-2'
    default:
      return 'text-chart-3'
  }
}

function RiskScoreGauge({
  summary,
  isLoading,
}: {
  summary?: RiskScoreSummary
  isLoading: boolean
}) {
  const score = summary?.overallScore ?? 0
  const clampedScore = Math.min(1000, Math.max(0, score))
  const needleAngle = 180 - (clampedScore / 1000) * 180
  const needleRadians = (needleAngle * Math.PI) / 180
  const needleX = 160 + 104 * Math.cos(needleRadians)
  const needleY = 160 - 104 * Math.sin(needleRadians)
  const bandLabel = summary ? riskGaugeLabel(score) : 'Preparing'
  const bandTone = summary ? riskGaugeTone(score) : 'text-muted-foreground'

  return (
    <div className="rounded-[1.6rem] border border-border/70 bg-background/60 p-5 shadow-sm backdrop-blur-sm">
      <div className="flex items-center justify-between gap-3">
        <div>
          <div className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
            Current total risk
          </div>
          <div className={cn('mt-1 text-sm font-semibold uppercase tracking-[0.16em]', bandTone)}>
            {bandLabel}
          </div>
        </div>
        <MetricInfoTooltip content="Total risk score is the tenant rollup across active asset risk. The gauge bands are contained, elevated, high, and critical." />
      </div>

      <div className="mt-5">
        <svg viewBox="0 0 320 190" role="img" aria-label={`Current total risk score ${summary ? Math.round(score) : 'loading'}`} className="h-auto w-full">
          <path d="M 36 160 A 124 124 0 0 1 284 160" pathLength="100" fill="none" stroke="var(--color-muted)" strokeWidth="18" strokeLinecap="round" opacity="0.45" />
          <path d="M 36 160 A 124 124 0 0 1 284 160" pathLength="100" fill="none" stroke="var(--color-chart-3)" strokeWidth="18" strokeLinecap="round" strokeDasharray="50 50" strokeDashoffset="0" />
          <path d="M 36 160 A 124 124 0 0 1 284 160" pathLength="100" fill="none" stroke="var(--color-chart-2)" strokeWidth="18" strokeLinecap="butt" strokeDasharray="25 75" strokeDashoffset="-50" />
          <path d="M 36 160 A 124 124 0 0 1 284 160" pathLength="100" fill="none" stroke="var(--color-tone-warning-foreground)" strokeWidth="18" strokeLinecap="butt" strokeDasharray="15 85" strokeDashoffset="-75" />
          <path d="M 36 160 A 124 124 0 0 1 284 160" pathLength="100" fill="none" stroke="var(--color-destructive)" strokeWidth="18" strokeLinecap="round" strokeDasharray="10 90" strokeDashoffset="-90" />
          {summary ? (
            <>
              <line x1="160" y1="160" x2={needleX} y2={needleY} stroke="currentColor" strokeWidth="3" strokeLinecap="round" className="text-foreground" />
              <circle cx="160" cy="160" r="7" fill="currentColor" className="text-foreground" />
            </>
          ) : null}
          <text x="40" y="184" className="fill-muted-foreground text-[10px] uppercase tracking-[0.14em]">Contained</text>
          <text x="230" y="184" className="fill-muted-foreground text-[10px] uppercase tracking-[0.14em]">Critical</text>
        </svg>
      </div>

      <div className="mt-[-0.5rem] flex items-end justify-between gap-4">
        <div>
          <div className={cn('text-5xl font-semibold tracking-[-0.05em]', bandTone)}>
            {summary ? Math.round(score) : isLoading ? '...' : 'N/A'}
          </div>
          <div className="mt-1 text-xs text-muted-foreground">
            {summary?.calculatedAt ? `Calculated ${new Date(summary.calculatedAt).toLocaleString()}` : 'Awaiting risk rollup'}
          </div>
        </div>
        <div className="text-right text-xs text-muted-foreground">
          <div>{summary?.assetCount ?? 0} scored assets</div>
          <div>{summary?.criticalAssetCount ?? 0} critical / {summary?.highAssetCount ?? 0} high</div>
        </div>
      </div>
      <div className="mt-4 grid grid-cols-4 gap-1 text-[10px] uppercase tracking-[0.12em] text-muted-foreground">
        <span className="border-t-2 border-chart-3 pt-1">Contained</span>
        <span className="border-t-2 border-chart-2 pt-1">Elevated</span>
        <span className="border-t-2 border-tone-warning-border pt-1">High</span>
        <span className="border-t-2 border-destructive pt-1">Critical</span>
      </div>
    </div>
  )
}

function ExecutiveSignalCard({
  label,
  tooltip,
  value,
  detail,
  tone,
  icon: Icon,
}: {
  label: string
  tooltip: string
  value: string
  detail: string
  tone: ExecutiveTone
  icon: LucideIcon
}) {
  return (
    <Card className={cn(
      'border-border/70 bg-card/90',
      tone === 'critical' && 'border-destructive/35 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--destructive)_10%,var(--card)),var(--card))]',
      tone === 'watch' && 'border-primary/25 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--primary)_10%,var(--card)),var(--card))]',
    )}>
      <CardHeader className="gap-2">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-1.5">
            <CardDescription className="text-[11px] uppercase tracking-[0.18em]">{label}</CardDescription>
            <MetricInfoTooltip content={tooltip} />
          </div>
          <span className={cn(
            'flex size-9 items-center justify-center rounded-2xl border',
            tone === 'critical' && 'border-destructive/30 bg-destructive/10 text-destructive',
            tone === 'watch' && 'border-primary/25 bg-primary/10 text-primary',
            tone === 'contained' && 'border-chart-3/25 bg-chart-3/10 text-chart-3',
          )}>
            <Icon className="size-4" />
          </span>
        </div>
        <CardTitle className="text-3xl tracking-[-0.05em]">{value}</CardTitle>
      </CardHeader>
      <CardContent>
        <p className="text-sm leading-6 text-muted-foreground">{detail}</p>
      </CardContent>
    </Card>
  )
}

export function CisoExecutiveOverview({
  summary,
  trends,
  isLoading,
  filters,
}: Props) {
  const { selectedTenantId } = useTenantScope()
  const riskScoreQuery = useQuery({
    queryKey: ['risk-score', 'summary', selectedTenantId, filters.minAgeDays, filters.platform, filters.deviceGroup],
    queryFn: () => fetchRiskScoreSummary({ data: filters }),
    enabled: Boolean(selectedTenantId),
    placeholderData: (previousData) => previousData,
    staleTime: 30_000,
  })
  const tone = deriveTone(summary)
  const trend = getTrendDirection(trends)
  const criticalCount = summary.vulnerabilitiesBySeverity.Critical ?? 0
  const highCount = summary.vulnerabilitiesBySeverity.High ?? 0
  const topDeviceGroup = [...summary.vulnerabilitiesByDeviceGroup]
    .sort((left, right) => {
      const leftScore = left.currentRiskScore ?? (left.critical * 10 + left.high * 6 + left.medium * 2 + left.low)
      const rightScore = right.currentRiskScore ?? (right.critical * 10 + right.high * 6 + right.medium * 2 + right.low)
      return rightScore - leftScore
    })
    .at(0)
  const healthyDevices = Object.entries(summary.deviceHealthBreakdown)
    .filter(([key]) => key.toLowerCase().includes('active') || key.toLowerCase().includes('healthy'))
    .reduce((total, [, count]) => total + count, 0)
  const totalDevices = Object.values(summary.deviceHealthBreakdown).reduce((total, count) => total + count, 0)
  const resolvedVsAppeared = summary.riskChangeBrief.resolvedCount - summary.riskChangeBrief.appearedCount
  const movementIsImproving = resolvedVsAppeared > 0 && trend.direction !== 'up'
  const movementTone: ExecutiveTone = trend.direction === 'up' || resolvedVsAppeared < 0
    ? 'watch'
    : movementIsImproving
      ? 'contained'
      : 'contained'

  return (
    <section className="space-y-6 pb-4">
      <Card className={cn(
        'overflow-hidden rounded-[2rem] border border-border/70 shadow-[0_30px_80px_-50px_rgba(0,0,0,0.55)]',
        tone.tone === 'critical' && 'bg-[linear-gradient(135deg,color-mix(in_oklab,var(--destructive)_18%,var(--background)),var(--card)_55%,var(--background))]',
        tone.tone === 'watch' && 'bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_20%,var(--background)),var(--card)_55%,var(--background))]',
        tone.tone === 'contained' && 'bg-[linear-gradient(135deg,color-mix(in_oklab,var(--chart-3)_18%,var(--background)),var(--card)_55%,var(--background))]',
      )}>
        <CardContent className="p-6 sm:p-8">
          <div className="grid gap-6 xl:grid-cols-[minmax(0,1.55fr)_minmax(20rem,0.85fr)]">
            <div className="space-y-5">
              <div className="flex flex-wrap items-center gap-3">
                <Badge
                  variant="outline"
                  className={cn(
                    'rounded-full border px-3 py-1 text-[11px] uppercase tracking-[0.18em]',
                    tone.tone === 'critical' && 'border-destructive/40 text-destructive',
                    tone.tone === 'watch' && 'border-primary/35 text-primary',
                    tone.tone === 'contained' && 'border-chart-3/35 text-chart-3',
                  )}
                >
                  Executive Security Brief
                </Badge>
                <Badge variant="outline" className="rounded-full px-3 py-1 text-xs text-muted-foreground">
                  Reporting scope: current tenant
                </Badge>
              </div>

              <div className="space-y-3">
                <h1 className="max-w-4xl text-3xl font-semibold tracking-[-0.06em] text-foreground sm:text-4xl">
                  {tone.headline}
                </h1>
                <p className="max-w-3xl text-base leading-7 text-muted-foreground">
                  {tone.narrative}
                </p>
              </div>

              <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
                <div className="rounded-[1.4rem] border border-border/70 bg-background/50 px-4 py-4 backdrop-blur-sm">
                  <div className="flex items-center gap-1.5 text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Critical exposure<MetricInfoTooltip content="Exposure is the part of organizational risk that is currently reachable through unresolved vulnerabilities. Critical exposure is the subset sitting in the highest severity band." /></div>
                  <div className="mt-2 text-4xl font-semibold tracking-[-0.06em]">{criticalCount}</div>
                  <div className="mt-2 text-sm text-muted-foreground">currently critical vulnerabilities across the estate</div>
                </div>
                <div className={cn(
                  'rounded-[1.4rem] border border-border/70 bg-background/50 px-4 py-4 backdrop-blur-sm',
                  movementTone === 'watch' && 'border-primary/25 bg-primary/6',
                )}>
                  <div className="flex items-center gap-1.5 text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Risk movement<MetricInfoTooltip content="Risk movement combines remediation momentum with the recent open-pressure trend, so leaders can see whether closure is outpacing new exposure and whether pressure is still rising." /></div>
                  <div className="mt-2 flex flex-wrap items-end gap-3">
                    <span className="text-4xl font-semibold tracking-[-0.06em]">{resolvedVsAppeared > 0 ? '+' : ''}{resolvedVsAppeared}</span>
                    <span className="pb-1 text-sm text-muted-foreground">net weekly movement</span>
                    <span className="flex items-center gap-1 pb-1 text-sm font-medium">
                      {trend.direction === 'down' ? <ArrowDownRight className="size-4 text-chart-3" /> : null}
                      {trend.direction === 'up' ? <ArrowUpRight className="size-4 text-destructive" /> : null}
                      {trend.direction === 'flat' ? <RefreshCw className="size-3.5 text-muted-foreground" /> : null}
                      {Math.abs(trend.delta)} trend
                    </span>
                  </div>
                  <div className="mt-2 text-sm text-muted-foreground">Resolved minus newly appeared vulnerabilities, paired with recent open-pressure direction.</div>
                </div>
                <div className="rounded-[1.4rem] border border-border/70 bg-background/50 px-4 py-4 backdrop-blur-sm">
                  <div className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Top pressure area</div>
                  <div className="mt-2 text-lg font-medium tracking-tight">
                    {topDeviceGroup?.deviceGroupName ?? 'No device-group pressure detected'}
                  </div>
                  <div className="mt-1 text-sm text-muted-foreground">
                    {describePressureArea(topDeviceGroup)}
                  </div>
                </div>
                <div className="rounded-[1.4rem] border border-border/70 bg-background/50 px-4 py-4 backdrop-blur-sm">
                  <div className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Estate resilience</div>
                  <div className="mt-2 text-lg font-medium tracking-tight">
                    {healthyDevices}/{totalDevices} devices in healthy or active status
                  </div>
                  <div className="mt-1 text-sm text-muted-foreground">
                    {formatPercent(summary.slaCompliancePercent)} SLA compliance. Average remediation time is {formatDays(summary.averageRemediationDays)}.
                  </div>
                </div>
              </div>
            </div>

            <div className="space-y-4">
              <RiskScoreGauge summary={riskScoreQuery.data} isLoading={isLoading || riskScoreQuery.isFetching} />
              <div className="rounded-[1.6rem] border border-border/70 bg-background/55 p-5 backdrop-blur-sm">
              <div className="flex items-center gap-2 text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                <Building2 className="size-4" />
                Management Readout
              </div>
              <div className="mt-4 space-y-4">
                <div>
                  <div className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Operational discipline</div>
                  <div className="mt-1 text-lg font-medium tracking-tight">
                    {formatPercent(summary.slaCompliancePercent)} SLA compliance
                  </div>
                  <div className="mt-1 text-sm text-muted-foreground">
                    {summary.overdueTaskCount} overdue remediation actions requiring follow-through.
                  </div>
                </div>
                <Link to="/dashboard" className="mt-2 inline-flex w-full items-center justify-center rounded-md border border-input bg-background px-4 py-2 text-sm font-medium shadow-sm hover:bg-accent hover:text-accent-foreground">
                  Open operational dashboard
                </Link>
              </div>
              </div>
            </div>
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-4 xl:grid-cols-4">
        <ExecutiveSignalCard
          label="Critical backlog"
          tooltip="Backlog means unresolved exposure still waiting for control action. Critical backlog is the portion of that backlog in the highest severity tier."
          value={String(criticalCount)}
          detail={`${highCount} additional high-severity vulnerabilities are still active.`}
          tone={criticalCount > 0 ? 'critical' : highCount > 0 ? 'watch' : 'contained'}
          icon={Siren}
        />
        <ExecutiveSignalCard
          label="Repeat risk"
          tooltip="Repeat risk reflects control drift: vulnerabilities or weakness patterns that reappear after they should have been prevented or permanently reduced."
          value={formatPercent(summary.recurrenceRatePercent)}
          detail={`${summary.recurringVulnerabilityCount} recurring vulnerabilities suggest control drift or repeated patch slippage.`}
          tone={summary.recurrenceRatePercent >= 15 ? 'critical' : summary.recurrenceRatePercent >= 6 ? 'watch' : 'contained'}
          icon={RefreshCw}
        />
        <ExecutiveSignalCard
          label="Remediation pace"
          tooltip="Remediation pace is how quickly the organization closes exposure once it is present. Slower pace increases the time risk remains active in the environment."
          value={formatDays(summary.averageRemediationDays)}
          detail={`${summary.overdueTaskCount} actions are overdue, which is the cleanest indicator of execution strain.`}
          tone={summary.overdueTaskCount >= 25 ? 'critical' : summary.overdueTaskCount >= 10 ? 'watch' : 'contained'}
          icon={Clock3}
        />
        <ExecutiveSignalCard
          label="Estate health"
          tooltip="Estate health is a broad posture signal combining timely remediation and healthy device-state coverage. It is a resilience indicator rather than a raw vulnerability count."
          value={formatPercent(summary.slaCompliancePercent)}
          detail={`${healthyDevices} of ${totalDevices} devices report healthy or active posture signals.`}
          tone={summary.slaCompliancePercent < 70 ? 'critical' : summary.slaCompliancePercent < 85 ? 'watch' : 'contained'}
          icon={Trophy}
        />
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.4fr)_minmax(20rem,1fr)]">
        <Card className="rounded-[1.6rem] border-border/70">
          <CardHeader>
            <CardTitle>Board Reporting Priorities</CardTitle>
            <CardDescription>
              The exposures most likely to matter in a management meeting right now.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            {summary.topCriticalVulnerabilities.slice(0, 5).map((item, index) => (
              <div
                key={item.id}
                className="flex items-start justify-between gap-4 rounded-[1.2rem] border border-border/60 bg-background/40 px-4 py-3"
              >
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">#{index + 1}</span>
                    <Badge variant="outline" className="rounded-full px-2 py-0.5 text-[11px]">
                      {item.severity}
                    </Badge>
                  </div>
                  <div className="mt-2 font-medium tracking-tight">{item.externalId}</div>
                  <div className="mt-1 text-sm text-muted-foreground">{item.title}</div>
                </div>
                <div className="shrink-0 text-right">
                  <div className="text-2xl font-semibold tracking-[-0.05em]">{item.affectedAssetCount}</div>
                  <div className="text-xs uppercase tracking-[0.18em] text-muted-foreground">affected assets</div>
                  <Button
                    variant="ghost"
                    size="sm"
                    className="mt-2"
                    render={
                      <Link
                        to="/vulnerabilities/$id"
                        params={{ id: item.id }}
                      />
                    }
                  >
                    View detail
                  </Button>
                </div>
              </div>
            ))}
          </CardContent>
        </Card>

        <div className="grid gap-4">
          <Card className="rounded-[1.6rem] border-border/70">
            <CardHeader>
              <CardTitle>What Changed Recently</CardTitle>
              <CardDescription>
                Movement you can speak to in weekly or monthly leadership reporting.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="rounded-[1.2rem] border border-border/60 bg-background/40 px-4 py-3">
                <div className="flex items-center justify-between">
                  <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Newly appeared</span>
                  <span className="text-2xl font-semibold tracking-[-0.05em]">{summary.riskChangeBrief.appearedCount}</span>
                </div>
                <p className="mt-2 text-sm text-muted-foreground">
                  Fresh exposures discovered in the latest reporting window.
                </p>
              </div>
              <div className="rounded-[1.2rem] border border-border/60 bg-background/40 px-4 py-3">
                <div className="flex items-center justify-between">
                  <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Resolved</span>
                  <span className="text-2xl font-semibold tracking-[-0.05em]">{summary.riskChangeBrief.resolvedCount}</span>
                </div>
                <p className="mt-2 text-sm text-muted-foreground">
                  Vulnerabilities that dropped out of the current exposure picture.
                </p>
              </div>
              {summary.riskChangeBrief.aiSummary ? (
                <div className="rounded-[1.2rem] border border-primary/18 bg-primary/6 px-4 py-3">
                  <div className="flex items-center gap-2 text-xs uppercase tracking-[0.18em] text-primary">
                    <Flame className="size-3.5" />
                    Briefing note
                  </div>
                  <p className="mt-2 text-sm leading-6 text-muted-foreground">
                    {summary.riskChangeBrief.aiSummary}
                  </p>
                </div>
              ) : null}
            </CardContent>
          </Card>

          <Card className="rounded-[1.6rem] border-border/70">
            <CardHeader>
              <CardTitle>Infrastructure Pressure</CardTitle>
              <CardDescription>
                Highest-pressure device groups by current risk concentration.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-3">
              {summary.vulnerabilitiesByDeviceGroup.slice(0, 4).map((group) => {
                const total = group.critical + group.high + group.medium + group.low
                const urgentShare = total > 0 ? ((group.critical + group.high) / total) * 100 : 0
                return (
                  <div key={group.deviceGroupName} className="rounded-[1.2rem] border border-border/60 bg-background/40 px-4 py-3">
                    <div className="flex items-center justify-between gap-4">
                      <div className="min-w-0">
                        <div className="font-medium tracking-tight">{group.deviceGroupName}</div>
                        <div className="mt-1 text-sm text-muted-foreground">
                          {group.assetCount ?? 0} assets, {group.openEpisodeCount ?? total} open episodes
                        </div>
                      </div>
                      <div className="text-right">
                        <div className="text-lg font-semibold tracking-[-0.04em]">
                          {group.currentRiskScore ? Math.round(group.currentRiskScore) : group.critical + group.high}
                        </div>
                        <div className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                          {group.currentRiskScore ? 'risk score' : 'priority load'}
                        </div>
                      </div>
                    </div>
                    <div className="mt-3 h-2 rounded-full bg-muted">
                      <div
                        className="h-2 rounded-full bg-[linear-gradient(90deg,var(--color-destructive),var(--color-primary))]"
                        style={{ width: `${Math.min(Math.max(urgentShare, 4), 100)}%` }}
                      />
                    </div>
                  </div>
                )
              })}
            </CardContent>
          </Card>
        </div>
      </div>
    </section>
  )
}
