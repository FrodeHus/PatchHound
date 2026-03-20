import { Skeleton } from '@/components/ui/skeleton'
import { useQuery } from '@tanstack/react-query'
import { Shield, Target, ChevronRight } from 'lucide-react'
import { useState } from 'react'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { fetchSecureScoreSummary } from '@/api/secure-score.functions'
import type { ScoreSnapshot, SecureScoreSummary } from '@/api/secure-score.schemas'
import { scorePosture, postureBadge, postureIcon } from "@/lib/score-posture";
import { SecureScoreDetailDialog } from './SecureScoreDetailDialog'

type SecureScoreCardProps = {
  isLoading?: boolean
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
            <Skeleton className="h-40 min-w-0 flex-1 " />
            <Skeleton className="h-40 min-w-0 flex-1 " />
          </div>
        </CardContent>
      </Card>
    )
  }

  if (!summary) return null

  const posture = scorePosture(summary.overallScore, summary.targetScore);
  const meetsTarget = summary.overallScore <= summary.targetScore
  const scoreDelta = summary.overallScore - summary.targetScore

  return (
    <>
      <Card className="overflow-hidden rounded-[32px] border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_11%,transparent),transparent_56%),var(--color-card)] shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
        <CardContent className="p-5">
          <div className="flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between">
            {/* Score section */}
            <div className="min-w-0 flex-1 rounded-2xl border border-border/70 bg-background/30 p-4">
              <div className="flex items-start justify-between gap-4">
                <div>
                  <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
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
              {summary.history.length > 1 && (
                <div className="mt-4">
                  <Sparkline
                    data={summary.history}
                    targetScore={summary.targetScore}
                    tone={posture.tone}
                  />
                </div>
              )}
              <div className="mt-3 flex items-center justify-between gap-3">
                <Badge
                  className={`rounded-full border px-2.5 py-1 text-xs ${postureBadge(posture.tone)}`}
                >
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
            <div className="min-w-0 flex-1 rounded-2xl border border-border/70 bg-background/30 p-4">
              <div className="flex items-start justify-between gap-4">
                <div>
                  <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                    Target
                  </p>
                  <p className="mt-3 text-4xl font-semibold tracking-[-0.04em]">
                    {summary.targetScore.toFixed(0)}
                  </p>
                </div>
                <span
                  className={`flex size-12 items-center justify-center rounded-2xl border ${postureIcon(posture.tone)}`}
                >
                  <Target className="size-5" />
                </span>
              </div>

              <div className="mt-4 space-y-2.5">
                {/* Score vs target bar */}
                <div className="relative h-2.5 overflow-hidden rounded-full bg-muted/80">
                  <div
                    className={`absolute inset-y-0 left-0 rounded-full transition-all ${posture.tone === "success" ? "bg-chart-3" : posture.tone === "warning" ? "bg-tone-warning-foreground/80" : "bg-destructive/80"}`}
                    style={{
                      width: `${Math.min(100, (summary.overallScore / 100) * 100)}%`,
                    }}
                  />
                  <div
                    className="absolute inset-y-0 w-0.5 bg-foreground/50"
                    style={{ left: `${summary.targetScore}%` }}
                  />
                </div>

                <div className="flex items-center justify-between text-xs">
                  <Badge
                    variant="outline"
                    className="rounded-full border-border/70 bg-background/30 text-foreground"
                  >
                    {summary.assetCount} assets scored
                  </Badge>
                  <span
                    className={`font-medium ${posture.tone === "success" ? "text-chart-3" : posture.tone === "warning" ? "text-tone-warning-foreground" : "text-destructive"}`}
                  >
                    {meetsTarget ? "" : "+"}
                    {scoreDelta.toFixed(1)} vs target
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
  );
}

const sparklineW = 200
const sparklineH = 32
const sparklinePad = 2

function Sparkline({
  data,
  targetScore,
  tone,
}: {
  data: ScoreSnapshot[]
  targetScore: number
  tone: 'success' | 'warning' | 'danger'
}) {
  const scores = data.map((d) => d.overallScore)
  const min = Math.min(...scores, targetScore) - 2
  const max = Math.max(...scores, targetScore) + 2
  const range = max - min || 1

  const toX = (i: number) => sparklinePad + (i / (scores.length - 1)) * (sparklineW - sparklinePad * 2)
  const toY = (v: number) => sparklinePad + (1 - (v - min) / range) * (sparklineH - sparklinePad * 2)

  const points = scores.map((s, i) => `${toX(i)},${toY(s)}`).join(' ')
  const targetY = toY(targetScore)

  const strokeColor =
    tone === 'success'
      ? 'var(--color-chart-3)'
      : tone === 'warning'
        ? 'var(--color-tone-warning-foreground)'
        : 'var(--color-destructive)'

  return (
    <svg
      viewBox={`0 0 ${sparklineW} ${sparklineH}`}
      className="h-8 w-full"
      preserveAspectRatio="none"
    >
      <line
        x1={sparklinePad}
        y1={targetY}
        x2={sparklineW - sparklinePad}
        y2={targetY}
        stroke="currentColor"
        strokeOpacity={0.2}
        strokeDasharray="3 3"
        strokeWidth={1}
      />
      <polyline
        points={points}
        fill="none"
        stroke={strokeColor}
        strokeWidth={1.5}
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <circle
        cx={toX(scores.length - 1)}
        cy={toY(scores[scores.length - 1])}
        r={2.5}
        fill={strokeColor}
      />
    </svg>
  )
}
