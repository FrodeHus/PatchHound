import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ChevronRight, CircleQuestionMark, Flame, RefreshCw, ShieldAlert, Siren } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { InsetPanel } from '@/components/ui/inset-panel'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { Sparkline } from '@/components/features/dashboard/Sparkline'
import { fetchRiskScoreSummary, recalculateRiskScores } from '@/api/risk-score.functions'
import type { RiskScoreSummary } from '@/api/risk-score.schemas'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { RiskScoreDetailDialog } from './RiskScoreDetailDialog'

type RiskScoreCardProps = {
  isLoading?: boolean
  filters?: {
    minAgeDays?: number
    platform?: string
    deviceGroup?: string
  }
}

export function RiskScoreCard({ isLoading: parentLoading, filters }: RiskScoreCardProps) {
  const [detailOpen, setDetailOpen] = useState(false)
  const { selectedTenantId } = useTenantScope()
  const queryClient = useQueryClient()
  const hasFilters = Boolean(filters?.minAgeDays !== undefined || filters?.platform || filters?.deviceGroup)

  const { data, isFetching, isError, error, refetch } = useQuery({
    queryKey: ['risk-score', 'summary', selectedTenantId, filters?.minAgeDays, filters?.platform, filters?.deviceGroup],
    queryFn: () => fetchRiskScoreSummary({ data: filters }),
    enabled: Boolean(selectedTenantId),
    placeholderData: (previousData) => previousData,
    staleTime: 30_000,
  })
  const recalculateMutation = useMutation({
    mutationFn: () => recalculateRiskScores(),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['risk-score', 'summary', selectedTenantId] })
    },
  })

  const loading = parentLoading || (isFetching && !data)
  const summary: RiskScoreSummary | undefined = data

  if (loading && !summary) {
    return (
      <Card className="overflow-hidden rounded-[32px] border-border/70 bg-[linear-gradient(140deg,color-mix(in_oklab,var(--destructive)_10%,transparent),transparent_52%),var(--color-card)] shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
        <CardContent className="p-5">
          <div className="grid gap-4 lg:grid-cols-[minmax(0,1.45fr)_minmax(18rem,1fr)]">
            <Skeleton className="h-44 rounded-2xl" />
            <Skeleton className="h-44 rounded-2xl" />
          </div>
        </CardContent>
      </Card>
    )
  }

  if (!summary) {
    return (
      <Card className="overflow-hidden rounded-[32px] border-border/70 bg-[linear-gradient(140deg,color-mix(in_oklab,var(--destructive)_6%,transparent),transparent_52%),var(--color-card)] shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
        <CardContent className="p-5">
          <InsetPanel className="rounded-[24px] p-4">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
              Current Risk Score
            </p>
            <p className="mt-3 text-lg font-medium">
              {isError ? 'Risk score unavailable' : 'Risk score not ready yet'}
            </p>
            <p className="mt-2 text-sm text-muted-foreground">
              {isError
                ? error instanceof Error
                  ? error.message
                  : 'The risk summary request failed.'
                : 'Risk rollups are still being prepared for this tenant.'}
            </p>
            <div className="mt-4">
              <Button type="button" variant="outline" size="sm" onClick={() => void refetch()}>
                Retry
              </Button>
            </div>
          </InsetPanel>
        </CardContent>
      </Card>
    )
  }

  const posture = riskPosture(summary.overallScore)
  const trend = summary.history.map((item) => item.overallScore)
  const criticalShare = summary.assetCount > 0
    ? Math.min(100, (summary.criticalAssetCount / summary.assetCount) * 100)
    : 0
  const highShare = summary.assetCount > 0
    ? Math.min(100, ((summary.criticalAssetCount + summary.highAssetCount) / summary.assetCount) * 100)
    : 0
  const mediumAssetCount = Math.max(
    summary.assetCount - summary.criticalAssetCount - summary.highAssetCount,
    0,
  )
  const mediumShare = summary.assetCount > 0
    ? Math.min(100, ((summary.criticalAssetCount + summary.highAssetCount + mediumAssetCount) / summary.assetCount) * 100)
    : 0
  const topBandCount = summary.criticalAssetCount + summary.highAssetCount
  const mixExplainer = hasFilters
    ? 'The score is calculated from the filtered asset set. Medium-risk assets can still produce an elevated tenant score even when no assets are currently high or critical.'
    : 'Tenant risk is driven by the highest asset risk, the top five asset average, and additional weight from critical, high, medium, and low assets. Medium-risk assets can raise the score without increasing the high/critical counts.'
  const severityProgress = Math.min(100, Math.max(0, (summary.overallScore / 1000) * 100))

  return (
    <>
      <Card className={`overflow-hidden rounded-[32px] border ${posture.cardClass} shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]`}>
        <CardContent className="p-5">
          <div className="grid gap-4 lg:grid-cols-[minmax(0,1.45fr)_minmax(18rem,1fr)]">
            <InsetPanel className={`space-y-5 rounded-[24px] border ${posture.heroPanelClass} p-5`}>
              <div className="flex items-start justify-between gap-4">
                <div className="space-y-3">
                  <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                    Current Risk Score
                  </p>
                  <div className="flex flex-wrap items-end gap-3">
                    <p className="text-5xl font-semibold tracking-[-0.05em]">
                      {summary.overallScore.toFixed(0)}
                    </p>
                    <div className="pb-1">
                      <p className={`text-sm font-semibold uppercase tracking-[0.16em] ${posture.verdictTone}`}>
                        {posture.scaleLabel}
                      </p>
                      <p className="text-xs text-muted-foreground">
                        {posture.scaleSubtext}
                      </p>
                    </div>
                  </div>
                </div>
                <span className={`flex size-12 items-center justify-center rounded-2xl border ${posture.iconClass}`}>
                  <Flame className="size-5" />
                </span>
              </div>

              <div className="space-y-2">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge className={posture.badgeClass}>{posture.label}</Badge>
                  <span className={`text-sm font-medium ${posture.verdictTone}`}>
                    {posture.verdict}
                  </span>
                </div>
                <p className="max-w-2xl text-sm leading-6 text-muted-foreground">
                  {posture.guidance}
                </p>
              </div>

              <div className="rounded-2xl border border-border/70 bg-background/45 px-4 py-4">
                <div className="flex items-center justify-between gap-3">
                  <div>
                    <p className="text-xs uppercase tracking-[0.16em] text-muted-foreground">
                      Risk Severity
                    </p>
                    <p className="mt-1 text-sm text-muted-foreground">
                      Higher scores are worse. `0` means contained exposure, `1000` means critical pressure.
                    </p>
                  </div>
                  <div className="text-right">
                    <p className={`text-sm font-semibold ${posture.verdictTone}`}>{posture.scaleLabel}</p>
                    <p className="text-xs text-muted-foreground">{severityProgress.toFixed(0)}% of max pressure</p>
                  </div>
                </div>
                <div className="mt-4">
                  <div className="h-3 overflow-hidden rounded-full bg-muted/90">
                    <div
                      className={`h-full rounded-full ${posture.meterClass}`}
                      style={{ width: `${severityProgress}%` }}
                    />
                  </div>
                  <div className="mt-2 grid grid-cols-4 text-[11px] uppercase tracking-[0.14em] text-muted-foreground">
                    <span>Contained</span>
                    <span className="text-center">Elevated</span>
                    <span className="text-center">High</span>
                    <span className="text-right">Critical</span>
                  </div>
                </div>
              </div>

              <div className="flex flex-wrap items-center gap-2">
                <Badge className={posture.badgeClass}>{posture.label}</Badge>
                <span className="text-xs text-muted-foreground">
                  {hasFilters ? 'Filtered episode-backed exposure' : 'Episode-backed tenant exposure'} across {summary.assetCount} assets.
                </span>
                <span className="text-xs text-muted-foreground">
                  {summary.calculatedAt ? `Updated ${formatUtcTimestamp(summary.calculatedAt)}` : 'Not calculated yet'}
                </span>
              </div>

              {trend.length > 1 ? (
                <div className="rounded-2xl border border-border/70 bg-background/35 px-3 py-3">
                  <div className="flex items-center justify-between gap-3">
                    <p className="text-xs uppercase tracking-[0.16em] text-muted-foreground">
                      30-Day Pressure
                    </p>
                    <div className="flex items-center gap-2">
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        className="h-7 rounded-full px-2.5 text-xs text-muted-foreground hover:text-foreground"
                        onClick={() => recalculateMutation.mutate()}
                        disabled={recalculateMutation.isPending}
                      >
                        <RefreshCw className={`mr-1 size-3.5 ${recalculateMutation.isPending ? 'animate-spin' : ''}`} />
                        Recalculate risk
                      </Button>
                      <button
                        type="button"
                        onClick={() => setDetailOpen(true)}
                        className="flex items-center gap-1 text-xs text-muted-foreground transition-colors hover:text-foreground"
                      >
                        See drivers
                        <ChevronRight className="size-3.5" />
                      </button>
                    </div>
                  </div>
                  <Sparkline
                    data={trend}
                    width={320}
                    height={38}
                    strokeColor={posture.sparkColor}
                    fillColor={posture.sparkColor}
                    className="mt-3 h-10 w-full"
                  />
                </div>
              ) : hasFilters ? (
                <div className="rounded-2xl border border-dashed border-border/70 bg-background/25 px-3 py-3">
                  <p className="text-xs uppercase tracking-[0.16em] text-muted-foreground">
                    Filtered snapshot
                  </p>
                  <p className="mt-2 text-sm text-muted-foreground">
                    Historical risk trend is only available for tenant-wide summaries. This view is calculated live from the current filtered episode set.
                  </p>
                </div>
              ) : null}
            </InsetPanel>

            <InsetPanel className="space-y-4 rounded-[24px] p-4">
              <div>
                <div className="flex items-center justify-between gap-3">
                  <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                    Pressure Mix
                  </p>
                  <InfoTooltip content={mixExplainer} />
                </div>
                <div className="mt-3 flex items-end justify-between gap-3">
                  <div>
                    <p className="text-3xl font-semibold tracking-[-0.04em]">
                      {topBandCount}
                    </p>
                    <p className="text-xs text-muted-foreground">assets in high or critical bands</p>
                  </div>
                  <Badge variant="outline" className="rounded-full border-border/70 bg-background/30">
                    {summary.criticalAssetCount} critical
                  </Badge>
                </div>
              </div>

              <div className="space-y-2.5">
                <div>
                  <div className="mb-1.5 flex items-center justify-between text-xs">
                    <span className="flex items-center gap-1.5 text-destructive">
                      <Siren className="size-3.5" />
                      Critical share
                    </span>
                    <span className="tabular-nums text-muted-foreground">{criticalShare.toFixed(0)}%</span>
                  </div>
                  <div className="h-2 overflow-hidden rounded-full bg-muted/80">
                    <div className="h-full rounded-full bg-destructive/80" style={{ width: `${criticalShare}%` }} />
                  </div>
                </div>
                <div>
                  <div className="mb-1.5 flex items-center justify-between text-xs">
                    <span className="flex items-center gap-1.5 text-tone-warning-foreground">
                      <ShieldAlert className="size-3.5" />
                      High-or-worse share
                    </span>
                    <span className="tabular-nums text-muted-foreground">{highShare.toFixed(0)}%</span>
                  </div>
                  <div className="h-2 overflow-hidden rounded-full bg-muted/80">
                    <div
                      className="h-full rounded-full bg-tone-warning-foreground/80"
                      style={{ width: `${highShare}%` }}
                    />
                  </div>
                </div>
                <div>
                  <div className="mb-1.5 flex items-center justify-between text-xs">
                    <span className="flex items-center gap-1.5 text-chart-2">
                      <ShieldAlert className="size-3.5" />
                      Medium-or-worse share
                    </span>
                    <span className="tabular-nums text-muted-foreground">{mediumShare.toFixed(0)}%</span>
                  </div>
                  <div className="h-2 overflow-hidden rounded-full bg-muted/80">
                    <div
                      className="h-full rounded-full bg-chart-2/80"
                      style={{ width: `${mediumShare}%` }}
                    />
                  </div>
                </div>
              </div>

              <div className="grid gap-2 sm:grid-cols-3">
                <MetricTile label="Critical assets" value={summary.criticalAssetCount} tone="text-destructive" />
                <MetricTile label="High assets" value={summary.highAssetCount} tone="text-tone-warning-foreground" />
                <MetricTile label="Medium assets" value={mediumAssetCount} tone="text-chart-2" />
              </div>
            </InsetPanel>
          </div>
        </CardContent>
      </Card>

      <RiskScoreDetailDialog
        open={detailOpen}
        onOpenChange={setDetailOpen}
        summary={summary}
      />
    </>
  )
}

function InfoTooltip({ content }: { content: string }) {
  return (
    <Tooltip>
      <TooltipTrigger className="inline-flex items-center text-muted-foreground/80 transition-colors hover:text-foreground focus-visible:outline-none focus-visible:text-foreground">
        <CircleQuestionMark className="size-4" />
      </TooltipTrigger>
      <TooltipContent className="max-w-sm text-sm">
        {content}
      </TooltipContent>
    </Tooltip>
  )
}

function MetricTile({ label, value, tone }: { label: string; value: number; tone: string }) {
  return (
    <div className="rounded-xl border border-border/70 bg-background/35 px-3 py-3">
      <p className="text-xs uppercase tracking-[0.15em] text-muted-foreground">{label}</p>
      <p className={`mt-2 text-2xl font-semibold tracking-[-0.04em] ${tone}`}>{value}</p>
    </div>
  )
}

function formatUtcTimestamp(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return `${date.toISOString().slice(0, 16).replace('T', ' ')} UTC`
}

function riskPosture(score: number) {
  if (score >= 900) {
    return {
      label: 'Critical pressure',
      verdict: 'Needs immediate attention',
      guidance: 'Current exposure is concentrated in the worst band. Treat this as an active high-priority risk posture until the top drivers are reduced.',
      scaleLabel: 'Critical',
      scaleSubtext: 'Bad',
      verdictTone: 'text-destructive',
      badgeClass: 'border-destructive/25 bg-destructive/10 text-destructive',
      iconClass: 'border-destructive/25 bg-destructive/10 text-destructive',
      cardClass: 'border-destructive/20 bg-[linear-gradient(140deg,color-mix(in_oklab,var(--destructive)_14%,transparent),transparent_56%),var(--color-card)]',
      heroPanelClass: 'border-destructive/15 bg-[linear-gradient(160deg,color-mix(in_oklab,var(--destructive)_10%,transparent),transparent_60%),var(--color-background)]',
      meterClass: 'bg-[linear-gradient(90deg,var(--color-chart-3),var(--color-chart-2),var(--color-tone-warning-foreground),var(--color-destructive))]',
      sparkColor: 'var(--color-destructive)',
    }
  }
  if (score >= 750) {
    return {
      label: 'High pressure',
      verdict: 'Serious exposure needs attention',
      guidance: 'The tenant is carrying a meaningful concentration of high-risk exposure. Prioritize the top drivers before this posture hardens into critical pressure.',
      scaleLabel: 'High',
      scaleSubtext: 'Bad',
      verdictTone: 'text-tone-warning-foreground',
      badgeClass: 'border-tone-warning-border bg-tone-warning text-tone-warning-foreground',
      iconClass: 'border-tone-warning-border bg-tone-warning/40 text-tone-warning-foreground',
      cardClass: 'border-tone-warning-border/40 bg-[linear-gradient(140deg,color-mix(in_oklab,var(--color-tone-warning-foreground)_10%,transparent),transparent_56%),var(--color-card)]',
      heroPanelClass: 'border-tone-warning-border/35 bg-[linear-gradient(160deg,color-mix(in_oklab,var(--color-tone-warning-foreground)_8%,transparent),transparent_60%),var(--color-background)]',
      meterClass: 'bg-[linear-gradient(90deg,var(--color-chart-3),var(--color-chart-2),var(--color-tone-warning-foreground),var(--color-destructive))]',
      sparkColor: 'var(--color-tone-warning-foreground)',
    }
  }
  if (score >= 500) {
    return {
      label: 'Elevated',
      verdict: 'Watch closely and reduce pressure',
      guidance: 'This is not a contained posture. Medium-risk exposure and concentrated drivers are pushing the tenant upward, even if very few assets are currently high or critical.',
      scaleLabel: 'Elevated',
      scaleSubtext: 'Caution',
      verdictTone: 'text-chart-2',
      badgeClass: 'border-chart-2/25 bg-chart-2/10 text-chart-2',
      iconClass: 'border-chart-2/20 bg-chart-2/10 text-chart-2',
      cardClass: 'border-chart-2/25 bg-[linear-gradient(140deg,color-mix(in_oklab,var(--color-chart-2)_10%,transparent),transparent_56%),var(--color-card)]',
      heroPanelClass: 'border-chart-2/20 bg-[linear-gradient(160deg,color-mix(in_oklab,var(--color-chart-2)_8%,transparent),transparent_60%),var(--color-background)]',
      meterClass: 'bg-[linear-gradient(90deg,var(--color-chart-3),var(--color-chart-2),var(--color-tone-warning-foreground),var(--color-destructive))]',
      sparkColor: 'var(--color-chart-2)',
    }
  }
  return {
    label: 'Contained',
    verdict: 'Exposure is currently contained',
    guidance: 'The tenant is in the healthiest risk band. Keep pressure low by watching new drivers and preserving remediation velocity.',
    scaleLabel: 'Contained',
    scaleSubtext: 'Good',
    verdictTone: 'text-chart-3',
    badgeClass: 'border-chart-3/20 bg-chart-3/10 text-chart-3',
    iconClass: 'border-chart-3/20 bg-chart-3/10 text-chart-3',
    cardClass: 'border-chart-3/20 bg-[linear-gradient(140deg,color-mix(in_oklab,var(--color-chart-3)_10%,transparent),transparent_56%),var(--color-card)]',
    heroPanelClass: 'border-chart-3/20 bg-[linear-gradient(160deg,color-mix(in_oklab,var(--color-chart-3)_8%,transparent),transparent_60%),var(--color-background)]',
    meterClass: 'bg-[linear-gradient(90deg,var(--color-chart-3),var(--color-chart-2),var(--color-tone-warning-foreground),var(--color-destructive))]',
    sparkColor: 'var(--color-chart-3)',
  }
}
