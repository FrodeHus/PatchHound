import { ArrowUpRight, Clock3, Radar, TrendingDown, TrendingUp } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Progress } from '@/components/ui/progress'
import { Sparkline } from './Sparkline'

type ExposureSlaCardProps = {
  exposureScore: number
  slaCompliancePercent: number
  overdueCount: number
  totalCount: number
  slaComplianceTrend?: { date: string; percent: number }[]
  isLoading?: boolean
}

export function ExposureSlaCard({
  exposureScore,
  slaCompliancePercent,
  overdueCount,
  totalCount,
  slaComplianceTrend,
  isLoading,
}: ExposureSlaCardProps) {
  const roundedScore = Number(exposureScore.toFixed(1))
  const posture = roundedScore >= 80 ? 'Elevated' : roundedScore >= 50 ? 'Guarded' : 'Stable'
  const boundedPercent = Math.max(0, Math.min(100, Number(slaCompliancePercent.toFixed(1))))

  const trendData = slaComplianceTrend?.map((t) => t.percent) ?? []
  const trendDirection = trendData.length >= 2
    ? trendData[trendData.length - 1] - trendData[0]
    : 0

  return (
    <Card className="overflow-hidden rounded-[32px] border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_11%,transparent),transparent_56%),var(--color-card)] shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
      <CardContent className="p-5">
        {isLoading ? (
          <div className="flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between">
            <div className="h-40 min-w-0 flex-1 animate-pulse rounded-[24px] bg-muted/60" />
            <div className="h-40 min-w-0 flex-1 animate-pulse rounded-[24px] bg-muted/60" />
          </div>
        ) : (
          <div className="flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between">
            <div className="min-w-0 flex-1 rounded-[24px] border border-border/70 bg-background/35 p-4">
              <div className="flex items-start justify-between gap-4">
                <div>
                  <p className="text-xs uppercase tracking-[0.24em] text-muted-foreground">Exposure score</p>
                  <p className="mt-3 text-5xl font-semibold tracking-[-0.04em]">{roundedScore}</p>
                </div>
                <span className="flex size-12 items-center justify-center rounded-2xl border border-primary/20 bg-primary/12 text-primary">
                  <Radar className="size-5" />
                </span>
              </div>
              <div className="mt-5 flex items-center justify-between gap-3">
                <Badge className="rounded-full border border-primary/20 bg-primary/12 px-2.5 py-1 text-xs text-primary hover:bg-primary/12">
                  {posture}
                </Badge>
                <span className="flex items-center gap-1 text-xs text-muted-foreground">
                  Composite risk index
                  <ArrowUpRight className="size-3.5" />
                </span>
              </div>
            </div>

            <div className="min-w-0 flex-1 rounded-[24px] border border-border/70 bg-background/35 p-4">
              <div className="flex items-start justify-between gap-4">
                <div>
                  <p className="text-xs uppercase tracking-[0.24em] text-muted-foreground">SLA compliance</p>
                  <p className="mt-3 text-4xl font-semibold tracking-[-0.04em]">{boundedPercent}%</p>
                </div>
                <span className="flex size-12 items-center justify-center rounded-2xl border border-chart-2/20 bg-chart-2/10 text-chart-2">
                  <Clock3 className="size-5" />
                </span>
              </div>
              <Progress value={boundedPercent} className="mt-6 h-2.5 rounded-full bg-muted/80" />
              {trendData.length >= 2 ? (
                <div className="mt-4 flex items-center gap-3">
                  <Sparkline
                    data={trendData}
                    width={100}
                    height={28}
                    strokeColor={trendDirection >= 0 ? 'var(--tone-success-foreground)' : 'var(--tone-danger-foreground)'}
                    fillColor={trendDirection >= 0 ? 'var(--tone-success-foreground)' : 'var(--tone-danger-foreground)'}
                  />
                  <span className="flex items-center gap-1 text-xs text-muted-foreground">
                    {trendDirection >= 0 ? (
                      <TrendingUp className="size-3.5 text-tone-success-foreground" />
                    ) : (
                      <TrendingDown className="size-3.5 text-tone-danger-foreground" />
                    )}
                    {trendDirection >= 0 ? '+' : ''}{trendDirection.toFixed(1)}% over 30d
                  </span>
                </div>
              ) : null}
              <div className="mt-3 flex items-center justify-between gap-3 text-xs">
                <Badge variant="outline" className="rounded-full border-border/70 bg-background/30 text-foreground">
                  {totalCount} tracked tasks
                </Badge>
                <span className="text-muted-foreground">{overdueCount} overdue actions</span>
              </div>
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  )
}
