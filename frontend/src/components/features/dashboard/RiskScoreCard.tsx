import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ChevronRight, Flame, RefreshCw, ShieldAlert, Siren } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { InsetPanel } from '@/components/ui/inset-panel'
import { Sparkline } from '@/components/features/dashboard/Sparkline'
import { fetchRiskScoreSummary, recalculateRiskScores } from '@/api/risk-score.functions'
import type { RiskScoreSummary } from '@/api/risk-score.schemas'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { RiskScoreDetailDialog } from './RiskScoreDetailDialog'

type RiskScoreCardProps = {
  isLoading?: boolean
}

export function RiskScoreCard({ isLoading: parentLoading }: RiskScoreCardProps) {
  const [detailOpen, setDetailOpen] = useState(false)
  const { selectedTenantId } = useTenantScope()
  const queryClient = useQueryClient()

  const { data, isFetching } = useQuery({
    queryKey: ['risk-score', 'summary', selectedTenantId],
    queryFn: () => fetchRiskScoreSummary(),
    staleTime: 30_000,
  })
  const recalculateMutation = useMutation({
    mutationFn: () => recalculateRiskScores(),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['risk-score', 'summary', selectedTenantId] })
    },
  })

  const loading = parentLoading || isFetching
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

  if (!summary) return null

  const posture = riskPosture(summary.overallScore)
  const trend = summary.history.map((item) => item.overallScore)
  const criticalShare = summary.assetCount > 0
    ? Math.min(100, (summary.criticalAssetCount / summary.assetCount) * 100)
    : 0
  const highShare = summary.assetCount > 0
    ? Math.min(100, ((summary.criticalAssetCount + summary.highAssetCount) / summary.assetCount) * 100)
    : 0

  return (
    <>
      <Card className="overflow-hidden rounded-[32px] border-border/70 bg-[linear-gradient(140deg,color-mix(in_oklab,var(--destructive)_10%,transparent),transparent_52%),var(--color-card)] shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
        <CardContent className="p-5">
          <div className="grid gap-4 lg:grid-cols-[minmax(0,1.45fr)_minmax(18rem,1fr)]">
            <InsetPanel className="space-y-4 rounded-[24px] p-4">
              <div className="flex items-start justify-between gap-4">
                <div>
                  <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                    Current Risk Score
                  </p>
                  <p className="mt-3 text-5xl font-semibold tracking-[-0.05em]">
                    {summary.overallScore.toFixed(0)}
                  </p>
                </div>
                <span className={`flex size-12 items-center justify-center rounded-2xl border ${posture.iconClass}`}>
                  <Flame className="size-5" />
                </span>
              </div>

              <div className="flex flex-wrap items-center gap-2">
                <Badge className={posture.badgeClass}>{posture.label}</Badge>
                <span className="text-xs text-muted-foreground">
                  Episode-backed tenant exposure across {summary.assetCount} assets.
                </span>
                <span className="text-xs text-muted-foreground">
                  {summary.calculatedAt ? `Updated ${new Date(summary.calculatedAt).toLocaleString()}` : 'Not calculated yet'}
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
              ) : null}
            </InsetPanel>

            <InsetPanel className="space-y-4 rounded-[24px] p-4">
              <div>
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                  Pressure Mix
                </p>
                <div className="mt-3 flex items-end justify-between gap-3">
                  <div>
                    <p className="text-3xl font-semibold tracking-[-0.04em]">
                      {summary.criticalAssetCount + summary.highAssetCount}
                    </p>
                    <p className="text-xs text-muted-foreground">assets in the top bands</p>
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
              </div>

              <div className="grid gap-2 sm:grid-cols-2">
                <MetricTile label="Critical assets" value={summary.criticalAssetCount} tone="text-destructive" />
                <MetricTile label="High assets" value={summary.highAssetCount} tone="text-tone-warning-foreground" />
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

function MetricTile({ label, value, tone }: { label: string; value: number; tone: string }) {
  return (
    <div className="rounded-xl border border-border/70 bg-background/35 px-3 py-3">
      <p className="text-xs uppercase tracking-[0.15em] text-muted-foreground">{label}</p>
      <p className={`mt-2 text-2xl font-semibold tracking-[-0.04em] ${tone}`}>{value}</p>
    </div>
  )
}

function riskPosture(score: number) {
  if (score >= 900) {
    return {
      label: 'Critical pressure',
      badgeClass: 'border-destructive/25 bg-destructive/10 text-destructive',
      iconClass: 'border-destructive/25 bg-destructive/10 text-destructive',
      sparkColor: 'var(--color-destructive)',
    }
  }
  if (score >= 750) {
    return {
      label: 'High pressure',
      badgeClass: 'border-tone-warning-border bg-tone-warning text-tone-warning-foreground',
      iconClass: 'border-tone-warning-border bg-tone-warning/40 text-tone-warning-foreground',
      sparkColor: 'var(--color-tone-warning-foreground)',
    }
  }
  if (score >= 500) {
    return {
      label: 'Elevated',
      badgeClass: 'border-chart-2/25 bg-chart-2/10 text-chart-2',
      iconClass: 'border-chart-2/20 bg-chart-2/10 text-chart-2',
      sparkColor: 'var(--color-chart-2)',
    }
  }
  return {
    label: 'Contained',
    badgeClass: 'border-chart-3/20 bg-chart-3/10 text-chart-3',
    iconClass: 'border-chart-3/20 bg-chart-3/10 text-chart-3',
    sparkColor: 'var(--color-chart-3)',
  }
}
