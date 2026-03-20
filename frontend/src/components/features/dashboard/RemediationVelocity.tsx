import { Skeleton } from '@/components/ui/skeleton'
import {
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { Gauge } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'

type RemediationVelocityProps = {
  averageDays: number
  vulnerabilitiesBySeverity: Record<string, number>
  isLoading?: boolean
}

type VelocityPoint = {
  severity: string
  volume: number
}

function getChartData(vulnerabilitiesBySeverity: Record<string, number>): VelocityPoint[] {
  return ['Low', 'Medium', 'High', 'Critical'].map((severity) => ({
    severity,
    volume: vulnerabilitiesBySeverity[severity] ?? 0,
  }))
}

export function RemediationVelocity({ averageDays, vulnerabilitiesBySeverity, isLoading }: RemediationVelocityProps) {
  const chartData = getChartData(vulnerabilitiesBySeverity)

  return (
    <Card className="rounded-[32px] border-border/70 bg-card/92 shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
      <CardHeader className="flex flex-row items-start justify-between space-y-0 p-5 pb-0">
        <div>
          <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Remediation velocity</p>
          <CardTitle className="mt-2 text-xl font-semibold tracking-tight">Severity backlog mix</CardTitle>
        </div>
        <div className="flex items-center gap-2">
          <Badge variant="outline" className="rounded-full border-border/70 bg-background/30 text-foreground">
            Avg {Number(averageDays.toFixed(1))} days
          </Badge>
          <span className="flex size-11 items-center justify-center rounded-2xl border border-chart-3/20 bg-chart-3/10 text-chart-3">
            <Gauge className="size-5" />
          </span>
        </div>
      </CardHeader>
      <CardContent className="pt-4">
        {isLoading ? (
          <Skeleton className="h-[250px] w-full " />
        ) : (
          <div className="h-[250px] w-full">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={chartData}>
              <CartesianGrid vertical={false} stroke="color-mix(in oklab, var(--border) 85%, transparent)" />
              <XAxis dataKey="severity" axisLine={false} tickLine={false} tick={{ fill: 'var(--color-muted-foreground)', fontSize: 12 }} />
              <YAxis allowDecimals={false} axisLine={false} tickLine={false} tick={{ fill: 'var(--color-muted-foreground)', fontSize: 12 }} />
              <Tooltip
                cursor={{ fill: 'color-mix(in oklab, var(--accent) 32%, transparent)' }}
                contentStyle={{
                  background: 'var(--color-popover)',
                  border: '1px solid color-mix(in oklab, var(--border) 90%, transparent)',
                  borderRadius: '16px',
                  color: 'var(--color-popover-foreground)',
                }}
              />
              <Bar dataKey="volume" fill="var(--color-primary)" radius={[10, 10, 4, 4]} />
            </BarChart>
          </ResponsiveContainer>
          </div>
        )}
      </CardContent>
    </Card>
  )
}
