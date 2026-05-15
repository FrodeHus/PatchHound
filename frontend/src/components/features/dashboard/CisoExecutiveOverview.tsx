import { ArrowDownRight, ArrowUpRight, Building2, Clock3, Flame, RefreshCw, Siren, Trophy } from 'lucide-react'
import { Link } from '@tanstack/react-router'
import { PolarAngleAxis, RadialBar, RadialBarChart } from 'recharts'
import type { DashboardSummary, ExecutiveExposureSummary, TrendData } from '@/api/dashboard.schemas'
import { MetricInfoTooltip } from '@/components/features/dashboard/MetricInfoTooltip'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { RISK_SCORE_RANGES, riskGaugeBand as deriveRiskGaugeBand } from '@/lib/risk-scoring'
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

const riskGaugeBands: Array<{
  band: RiskGaugeBand
  label: string
  range: string
  colorClass: string
  borderClass: string
  fill: string
}> = [
  {
    band: 'contained',
    label: 'Contained',
    range: RISK_SCORE_RANGES.low,
    colorClass: 'text-chart-3',
    borderClass: 'border-chart-3',
    fill: 'var(--color-chart-3)',
  },
  {
    band: 'elevated',
    label: 'Elevated',
    range: RISK_SCORE_RANGES.medium,
    colorClass: 'text-chart-2',
    borderClass: 'border-chart-2',
    fill: 'var(--color-chart-2)',
  },
  {
    band: 'high',
    label: 'High',
    range: RISK_SCORE_RANGES.high,
    colorClass: 'text-tone-warning-foreground',
    borderClass: 'border-tone-warning-border',
    fill: 'var(--color-tone-warning-foreground)',
  },
  {
    band: 'critical',
    label: 'Critical',
    range: RISK_SCORE_RANGES.critical,
    colorClass: 'text-destructive',
    borderClass: 'border-destructive',
    fill: 'var(--color-destructive)',
  },
]

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
  const exposure = summary.executiveExposure
  if (exposure?.riskLevel === 'Critical') {
    return {
      tone: 'critical',
      headline: 'Cyber exposure needs leadership attention',
      narrative: exposure.topDriverDetail ?? 'The current exposure score is in the critical band and should be actively managed through the next reporting cycle.',
    }
  }

  if (exposure?.riskLevel === 'High' || exposure?.trend === 'Worsening') {
    return {
      tone: 'watch',
      headline: 'Cyber exposure is elevated',
      narrative: exposure.topDriverDetail ?? 'The current score or recent movement shows enough pressure to warrant management follow-through.',
    }
  }

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

function normalizeRiskLevel(level: string | undefined): RiskGaugeBand {
  const normalized = level?.toLowerCase()
  if (normalized === 'critical' || normalized === 'high' || normalized === 'elevated' || normalized === 'contained') {
    return normalized
  }

  return 'contained'
}

function riskGaugeBandMeta(score: number) {
  return riskGaugeBands.find((band) => band.band === deriveRiskGaugeBand(score)) ?? riskGaugeBands[0]
}

function describeExecutiveTrend(exposure: ExecutiveExposureSummary | null | undefined) {
  if (!exposure) {
    return 'Awaiting risk rollup'
  }

  if (exposure.trend === 'Filtered') {
    return 'Trend hidden for filtered scope'
  }

  if (exposure.scoreDelta === null) {
    return 'No baseline yet'
  }

  const direction = exposure.scoreDelta > 0 ? '+' : ''
  return `${direction}${Math.round(exposure.scoreDelta)} vs prior snapshot`
}

function RiskScoreGauge({
  exposure,
  isLoading,
}: {
  exposure?: ExecutiveExposureSummary | null
  isLoading: boolean
}) {
  const score = exposure?.score ?? 0
  const clampedScore = Math.min(1000, Math.max(0, score))
  const activeBand = exposure
    ? riskGaugeBands.find((band) => band.band === normalizeRiskLevel(exposure.riskLevel)) ?? riskGaugeBandMeta(score)
    : riskGaugeBandMeta(score)
  const bandLabel = exposure ? activeBand.label : 'Preparing'
  const bandTone = exposure ? activeBand.colorClass : 'text-muted-foreground'
  const chartData = [{ score: clampedScore, fill: exposure ? activeBand.fill : 'var(--color-muted-foreground)' }]

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

      <div
        className="relative mt-5 flex h-[190px] items-center justify-center"
        role="img"
        aria-label={`Current total risk score ${exposure ? Math.round(score) : 'loading'} with ${bandLabel.toLowerCase()} status`}
      >
        <RadialBarChart
          width={280}
          height={190}
          data={chartData}
          cx={140}
          cy={142}
          innerRadius={86}
          outerRadius={118}
          barSize={18}
          startAngle={180}
          endAngle={0}
        >
          <PolarAngleAxis type="number" domain={[0, 1000]} tick={false} />
          <RadialBar
            dataKey="score"
            background={{ fill: 'var(--color-muted)', opacity: 0.42 }}
            cornerRadius={12}
            className="drop-shadow-sm"
          />
        </RadialBarChart>
        <div className="pointer-events-none absolute inset-x-0 bottom-6 text-center">
          <div className={cn('text-5xl font-semibold tracking-[-0.05em]', bandTone)}>
            {exposure ? Math.round(score) : isLoading ? '...' : 'N/A'}
          </div>
          <div className="mt-1 text-xs uppercase tracking-[0.16em] text-muted-foreground">
            Current risk score
          </div>
        </div>
      </div>

      <div className="mt-1 flex items-end justify-between gap-4">
        <div className="text-xs text-muted-foreground">
          {describeExecutiveTrend(exposure)}
        </div>
        <div className="text-right text-xs text-muted-foreground">
          <div>{exposure?.assetCount ?? 0} scored assets</div>
          <div>{exposure?.criticalAssetCount ?? 0} critical / {exposure?.highAssetCount ?? 0} high</div>
        </div>
      </div>
      <div className="mt-4 grid grid-cols-4 gap-1 text-[10px] uppercase tracking-[0.12em]">
        {riskGaugeBands.map((band) => {
          const isActive = exposure && band.band === activeBand.band
          return (
            <div
              key={band.band}
              className={cn(
                'rounded-lg border-t-2 bg-background/35 px-2 py-2 text-muted-foreground',
                band.borderClass,
                isActive && `${band.colorClass} bg-muted/45`,
              )}
            >
              <div className="font-semibold">{band.label}</div>
              <div className="mt-0.5 text-[9px] tracking-[0.08em] opacity-75">{band.range}</div>
            </div>
          )
        })}
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

function sourceTone(source: string) {
  switch (source) {
    case 'Rule':
      return 'border-chart-3/30 text-chart-3'
    case 'Manual':
      return 'border-primary/30 text-primary'
    case 'Default':
      return 'border-tone-warning-border text-tone-warning-foreground'
    case 'Unowned':
      return 'border-destructive/35 text-destructive'
    default:
      return 'border-border text-muted-foreground'
  }
}

export function CisoExecutiveOverview({
  summary,
  trends,
  isLoading,
  filters: _filters,
}: Props) {
  const tone = deriveTone(summary)
  const trend = getTrendDirection(trends)
  const executiveExposure = summary.executiveExposure
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
  const accountability = summary.accountability
  const accountabilityOpenWork = (accountability?.awaitingDecisionCount ?? 0)
    + (accountability?.overdueApprovalCount ?? 0)
    + (accountability?.overduePatchingTaskCount ?? 0)
  const unownedCount = (accountability?.unownedAssetCount ?? 0) + (accountability?.unownedSoftwareCount ?? 0)
  const defaultRoutedCount = (accountability?.defaultRoutedAssetCount ?? 0) + (accountability?.defaultRoutedSoftwareCount ?? 0)

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

              {summary.riskChangeBrief.aiSummary ? (
                <div className="max-w-4xl border-l-2 border-primary/35 pl-4">
                  <div className="flex items-center gap-2 text-[11px] uppercase tracking-[0.18em] text-primary">
                    <Flame className="size-3.5" />
                    Briefing note
                  </div>
                  <p className="mt-2 whitespace-pre-line text-sm leading-6 text-foreground/80">
                    {summary.riskChangeBrief.aiSummary}
                  </p>
                </div>
              ) : null}

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
                    {executiveExposure?.topDriver ?? topDeviceGroup?.deviceGroupName ?? 'No active risk driver detected'}
                  </div>
                  <div className="mt-1 text-sm text-muted-foreground">
                    {executiveExposure?.topDriverDetail ?? describePressureArea(topDeviceGroup)}
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
              <RiskScoreGauge exposure={executiveExposure} isLoading={isLoading} />
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

      {accountability ? (
        <Card className="rounded-[1.6rem] border-border/70">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle>Ownership Accountability</CardTitle>
                <CardDescription>
                  Owner-level risk, unanswered workflow decisions, and routing gaps.
                </CardDescription>
              </div>
              <div className="flex flex-wrap gap-2">
                <Badge variant="outline" className={cn('rounded-full px-3 py-1 text-xs', unownedCount > 0 && 'border-destructive/35 text-destructive')}>
                  {unownedCount} unowned
                </Badge>
                <Badge variant="outline" className={cn('rounded-full px-3 py-1 text-xs', defaultRoutedCount > 0 && 'border-tone-warning-border text-tone-warning-foreground')}>
                  {defaultRoutedCount} default-routed
                </Badge>
                <Badge variant="outline" className={cn('rounded-full px-3 py-1 text-xs', accountabilityOpenWork > 0 && 'border-primary/35 text-primary')}>
                  {accountabilityOpenWork} blocked
                </Badge>
              </div>
            </div>
          </CardHeader>
          <CardContent className="space-y-3">
            {accountability.topOwners.slice(0, 5).map((owner) => {
              const urgentWork = owner.overduePatchingTaskCount + owner.overdueApprovalCount + owner.awaitingDecisionCount
              const routedCount = owner.manualOwnedAssetCount + owner.ruleOwnedAssetCount + owner.defaultRoutedAssetCount
                + owner.manualOwnedSoftwareCount + owner.ruleOwnedSoftwareCount + owner.defaultRoutedSoftwareCount
              return (
                <div key={owner.teamId ?? owner.ownerName} className="rounded-[1.2rem] border border-border/60 bg-background/40 px-4 py-3">
                  <div className="flex flex-wrap items-start justify-between gap-4">
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <div className="font-medium tracking-tight">{owner.ownerName}</div>
                        <Badge variant="outline" className={cn('rounded-full px-2 py-0.5 text-[11px]', sourceTone(owner.ownerAssignmentSource))}>
                          {owner.ownerAssignmentSource}
                        </Badge>
                      </div>
                      <div className="mt-1 text-sm text-muted-foreground">
                        {owner.criticalOpenExposureCount} critical, {owner.highOpenExposureCount} high, {urgentWork} decision or overdue items.
                      </div>
                    </div>
                    <div className="shrink-0 text-right">
                      <div className="text-2xl font-semibold tracking-[-0.05em]">{Math.round(owner.riskScore)}</div>
                      <div className="text-xs uppercase tracking-[0.18em] text-muted-foreground">owner risk</div>
                    </div>
                  </div>
                  <div className="mt-3 grid gap-2 text-xs text-muted-foreground sm:grid-cols-4">
                    <div>{owner.assetCount || owner.unownedAssetCount} assets</div>
                    <div>{routedCount || owner.unownedSoftwareCount} routed items</div>
                    <div>{owner.acceptedRiskCount} accepted risk</div>
                    <div>{owner.openEpisodeCount} open episodes</div>
                  </div>
                </div>
              )
            })}
          </CardContent>
        </Card>
      ) : null}

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
