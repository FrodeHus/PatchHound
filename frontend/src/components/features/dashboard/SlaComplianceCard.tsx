import { Clock3 } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Progress } from '@/components/ui/progress'

type SlaComplianceCardProps = {
  percent: number
  overdueCount: number
  totalCount: number
}

export function SlaComplianceCard({ percent, overdueCount, totalCount }: SlaComplianceCardProps) {
  const boundedPercent = Math.max(0, Math.min(100, Number(percent.toFixed(1))))

  return (
    <Card className="rounded-2xl border-border/70 bg-card/92 shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
      <CardContent className="p-5">
        <div className="flex items-start justify-between">
          <div>
            <p className="text-xs uppercase tracking-[0.24em] text-muted-foreground">SLA compliance</p>
            <p className="mt-3 text-4xl font-semibold tracking-[-0.04em]">{boundedPercent}%</p>
          </div>
          <span className="flex size-12 items-center justify-center rounded-2xl border border-chart-2/20 bg-chart-2/10 text-chart-2">
            <Clock3 className="size-5" />
          </span>
        </div>
        <Progress value={boundedPercent} className="mt-6 h-2.5 rounded-full bg-muted/80" />
        <div className="mt-4 flex items-center justify-between gap-3 text-xs">
          <Badge variant="outline" className="rounded-full border-border/70 bg-background/30 text-foreground">
            {totalCount} tracked tasks
          </Badge>
          <span className="text-muted-foreground">{overdueCount} overdue actions</span>
        </div>
      </CardContent>
    </Card>
  )
}
