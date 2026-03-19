import { useQuery } from '@tanstack/react-query'
import { Shield, Target, ChevronRight } from 'lucide-react'
import { useState } from 'react'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { fetchSecureScoreSummary } from '@/api/secure-score.functions'
import type { SecureScoreSummary } from '@/api/secure-score.schemas'
import { SecureScoreDetailDialog } from './SecureScoreDetailDialog'

type SecureScoreCardProps = {
  isLoading?: boolean
}

function scorePosture(score: number): { label: string; badgeClass: string } {
  if (score >= 75) return { label: 'Critical', badgeClass: 'border-tone-danger-border bg-tone-danger text-tone-danger-foreground' }
  if (score >= 50) return { label: 'Elevated', badgeClass: 'border-tone-warning-border bg-tone-warning text-tone-warning-foreground' }
  if (score >= 25) return { label: 'Guarded', badgeClass: 'border-tone-info-border bg-tone-info text-tone-info-foreground' }
  return { label: 'Stable', badgeClass: 'border-tone-success-border bg-tone-success text-tone-success-foreground' }
}

export function SecureScoreCard({ isLoading: parentLoading }: SecureScoreCardProps) {
  const [detailOpen, setDetailOpen] = useState(false)

  const { data, isFetching } = useQuery({
    queryKey: ['secure-score', 'summary'],
    queryFn: () => fetchSecureScoreSummary(),
    staleTime: 30_000,
  })

  const loading = parentLoading || isFetching
  const summary: SecureScoreSummary | undefined = data

  if (loading && !summary) {
    return (
      <Card className="overflow-hidden rounded-[32px] border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_11%,transparent),transparent_56%),var(--color-card)] shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
        <CardContent className="p-5">
          <div className="flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between">
            <div className="h-40 min-w-0 flex-1 animate-pulse rounded-[24px] bg-muted/60" />
            <div className="h-40 min-w-0 flex-1 animate-pulse rounded-[24px] bg-muted/60" />
          </div>
        </CardContent>
      </Card>
    )
  }

  if (!summary) return null

  const posture = scorePosture(summary.overallScore)
  const meetsTarget = summary.overallScore <= summary.targetScore
  const scoreDelta = summary.overallScore - summary.targetScore

  return (
    <>
      <Card className="overflow-hidden rounded-[32px] border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_11%,transparent),transparent_56%),var(--color-card)] shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
        <CardContent className="p-5">
          <div className="flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between">
            {/* Score section */}
            <div className="min-w-0 flex-1 rounded-[24px] border border-border/70 bg-background/35 p-4">
              <div className="flex items-start justify-between gap-4">
                <div>
                  <p className="text-xs uppercase tracking-[0.24em] text-muted-foreground">
                    Secure Score
                  </p>
                  <p className="mt-3 text-5xl font-semibold tracking-[-0.04em]">
                    {summary.overallScore.toFixed(1)}
                  </p>
                </div>
                <span className="flex size-12 items-center justify-center rounded-2xl border border-primary/20 bg-primary/12 text-primary">
                  <Shield className="size-5" />
                </span>
              </div>
              <div className="mt-5 flex items-center justify-between gap-3">
                <Badge className={`rounded-full border px-2.5 py-1 text-xs ${posture.badgeClass}`}>
                  {posture.label}
                </Badge>
                <button
                  type="button"
                  onClick={() => setDetailOpen(true)}
                  className="flex items-center gap-1 text-xs text-muted-foreground transition-colors hover:text-foreground"
                >
                  See breakdown
                  <ChevronRight className="size-3.5" />
                </button>
              </div>
            </div>

            {/* Target section */}
            <div className="min-w-0 flex-1 rounded-[24px] border border-border/70 bg-background/35 p-4">
              <div className="flex items-start justify-between gap-4">
                <div>
                  <p className="text-xs uppercase tracking-[0.24em] text-muted-foreground">
                    Target
                  </p>
                  <p className="mt-3 text-4xl font-semibold tracking-[-0.04em]">
                    {summary.targetScore.toFixed(0)}
                  </p>
                </div>
                <span className={`flex size-12 items-center justify-center rounded-2xl border ${meetsTarget ? 'border-chart-3/20 bg-chart-3/10 text-chart-3' : 'border-destructive/20 bg-destructive/10 text-destructive'}`}>
                  <Target className="size-5" />
                </span>
              </div>

              <div className="mt-4 space-y-2.5">
                {/* Score vs target bar */}
                <div className="relative h-2.5 overflow-hidden rounded-full bg-muted/80">
                  <div
                    className={`absolute inset-y-0 left-0 rounded-full transition-all ${meetsTarget ? 'bg-chart-3' : 'bg-destructive/80'}`}
                    style={{ width: `${Math.min(100, (summary.overallScore / 100) * 100)}%` }}
                  />
                  <div
                    className="absolute inset-y-0 w-0.5 bg-foreground/50"
                    style={{ left: `${summary.targetScore}%` }}
                  />
                </div>

                <div className="flex items-center justify-between text-xs">
                  <Badge variant="outline" className="rounded-full border-border/70 bg-background/30 text-foreground">
                    {summary.assetCount} assets scored
                  </Badge>
                  <span className={`font-medium ${meetsTarget ? 'text-chart-3' : 'text-destructive'}`}>
                    {meetsTarget ? '' : '+'}{scoreDelta.toFixed(1)} vs target
                  </span>
                </div>
              </div>
            </div>
          </div>
        </CardContent>
      </Card>

      <SecureScoreDetailDialog
        open={detailOpen}
        onOpenChange={setDetailOpen}
        summary={summary}
      />
    </>
  )
}
