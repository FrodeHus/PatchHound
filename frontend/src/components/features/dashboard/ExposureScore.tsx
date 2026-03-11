import { ArrowUpRight, Radar } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'

type ExposureScoreProps = {
  score: number
}

export function ExposureScore({ score }: ExposureScoreProps) {
  const roundedScore = Number(score.toFixed(1))
  const posture = roundedScore >= 80 ? 'Elevated' : roundedScore >= 50 ? 'Guarded' : 'Stable'

  return (
    <Card className="overflow-hidden rounded-2xl border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_14%,transparent),transparent_56%),var(--color-card)] shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
      <CardContent className="p-5">
        <div className="flex items-start justify-between">
          <div>
            <p className="text-xs uppercase tracking-[0.24em] text-muted-foreground">Exposure score</p>
            <p className="mt-3 text-5xl font-semibold tracking-[-0.04em]">{roundedScore}</p>
          </div>
          <span className="flex size-12 items-center justify-center rounded-2xl border border-primary/20 bg-primary/12 text-primary">
            <Radar className="size-5" />
          </span>
        </div>
        <div className="mt-5 flex items-center justify-between">
          <Badge className="rounded-full border border-primary/20 bg-primary/12 px-2.5 py-1 text-xs text-primary hover:bg-primary/12">
            {posture}
          </Badge>
          <span className="flex items-center gap-1 text-xs text-muted-foreground">
            Composite risk index
            <ArrowUpRight className="size-3.5" />
          </span>
        </div>
      </CardContent>
    </Card>
  )
}
