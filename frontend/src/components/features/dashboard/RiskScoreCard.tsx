import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ChevronRight, Flame, RefreshCw, ShieldAlert, Siren } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Sparkline } from '@/components/features/dashboard/Sparkline'
import { fetchRiskScoreSummary, recalculateRiskScores } from '@/api/risk-score.functions'
import type { RiskScoreSummary } from '@/api/risk-score.schemas'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { riskScoreBand } from '@/lib/risk-scoring'
import { MetricInfoTooltip } from './MetricInfoTooltip'
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
      <div className="grid grid-cols-[1.15fr_1px_1fr]">
        <Skeleton className="m-5 h-44 rounded-2xl" />
        <div className="my-4 bg-border/50" />
        <Skeleton className="m-5 h-44 rounded-2xl" />
      </div>
    )
  }

  if (!summary) {
    return (
      <div className="p-5">
        <div className="rounded-2xl border border-border/70 bg-muted/40 p-4">
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
        </div>
      </div>
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
      <div className="grid grid-cols-1 lg:grid-cols-[1.15fr_1px_1fr]">
        <div className={`space-y-5 p-5 lg:p-6 ${posture.heroPanelBg}`}>
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
        </div>

        {/* Vertical hairline divider */}
        <div
          className="hidden bg-[linear-gradient(180deg,transparent_0%,var(--color-border)_12%,var(--color-border)_88%,transparent_100%)] lg:block"
          aria-hidden
        />

        <div className="space-y-4 p-5 lg:p-6">
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
                <MetricTile label="Critical assets" tooltip="A risk band is PatchHound's severity grouping for current exposure on an asset. Critical is the highest band and reflects the strongest concentration of active risk, not business criticality." value={summary.criticalAssetCount} tone="text-destructive" />
                <MetricTile label="High assets" tooltip="High risk band means the asset carries serious active exposure, but below the platform's top critical threshold." value={summary.highAssetCount} tone="text-tone-warning-foreground" />
                <MetricTile label="Medium assets" tooltip="Medium risk band represents meaningful active exposure that is notable but not yet in the high or critical tiers." value={mediumAssetCount} tone="text-chart-2" />
              </div>
        </div>
      </div>

      <RiskScoreDetailDialog
        open={detailOpen}
        onOpenChange={setDetailOpen}
        summary={summary}
      />
    </>
  )
}

const InfoTooltip = MetricInfoTooltip

function MetricTile({ label, tooltip, value, tone }: { label: string; tooltip: string; value: number; tone: string }) {
  return (
    <div className="rounded-xl border border-border/70 bg-background/35 px-3 py-3">
      <div className="flex items-center gap-1.5">
        <p className="text-xs uppercase tracking-[0.15em] text-muted-foreground">{label}</p>
        <MetricInfoTooltip content={tooltip} />
      </div>
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
  const band = riskScoreBand(score)

  if (band === 'critical') {
    return {
      label: 'Critical pressure',
      verdict: 'Needs immediate attention',
      guidance: 'Current exposure is concentrated in the worst band. Treat this as an active high-priority risk posture until the top drivers are reduced.',
      scaleLabel: 'Critical',
      scaleSubtext: 'Bad',
      verdictTone: 'text-destructive',
      badgeClass: 'border-destructive/25 bg-destructive/10 text-destructive',
      iconClass: 'border-destructive/25 bg-destructive/10 text-destructive',
      heroPanelBg: 'bg-[linear-gradient(160deg,color-mix(in_oklab,var(--destructive)_10%,transparent),transparent_60%)]',
      meterClass: 'bg-[linear-gradient(90deg,var(--color-chart-3),var(--color-chart-2),var(--color-tone-warning-foreground),var(--color-destructive))]',
      sparkColor: 'var(--color-destructive)',
    }
  }
  if (band === 'high') {
    return {
      label: 'High pressure',
      verdict: 'Serious exposure needs attention',
      guidance: 'The tenant is carrying a meaningful concentration of high-risk exposure. Prioritize the top drivers before this posture hardens into critical pressure.',
      scaleLabel: 'High',
      scaleSubtext: 'Bad',
      verdictTone: 'text-tone-warning-foreground',
      badgeClass: 'border-tone-warning-border bg-tone-warning text-tone-warning-foreground',
      iconClass: 'border-tone-warning-border bg-tone-warning/40 text-tone-warning-foreground',
      heroPanelBg: 'bg-[linear-gradient(160deg,color-mix(in_oklab,var(--color-tone-warning-foreground)_8%,transparent),transparent_60%)]',
      meterClass: 'bg-[linear-gradient(90deg,var(--color-chart-3),var(--color-chart-2),var(--color-tone-warning-foreground),var(--color-destructive))]',
      sparkColor: 'var(--color-tone-warning-foreground)',
    }
  }
  if (band === 'medium') {
    return {
      label: 'Elevated',
      verdict: 'Watch closely and reduce pressure',
      guidance: 'This is not a contained posture. Medium-risk exposure and concentrated drivers are pushing the tenant upward, even if very few assets are currently high or critical.',
      scaleLabel: 'Elevated',
      scaleSubtext: 'Caution',
      verdictTone: 'text-chart-2',
      badgeClass: 'border-chart-2/25 bg-chart-2/10 text-chart-2',
      iconClass: 'border-chart-2/20 bg-chart-2/10 text-chart-2',
      heroPanelBg: 'bg-[linear-gradient(160deg,color-mix(in_oklab,var(--color-chart-2)_8%,transparent),transparent_60%)]',
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
    heroPanelBg: 'bg-[linear-gradient(160deg,color-mix(in_oklab,var(--color-chart-3)_8%,transparent),transparent_60%)]',
    meterClass: 'bg-[linear-gradient(90deg,var(--color-chart-3),var(--color-chart-2),var(--color-tone-warning-foreground),var(--color-destructive))]',
    sparkColor: 'var(--color-chart-3)',
  }
}
