import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { TrendData } from '@/api/dashboard.schemas'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'

type TrendChartProps = {
  data: TrendData
}

type ChartPoint = {
  date: string
  Low: number
  Medium: number
  High: number
  Critical: number
}

function formatChartData(data: TrendData): ChartPoint[] {
  const map = new Map<string, ChartPoint>()

  data.items.forEach((item) => {
    const existing = map.get(item.date) ?? {
      date: item.date,
      Low: 0,
      Medium: 0,
      High: 0,
      Critical: 0,
    }

    if (item.severity === 'Low' || item.severity === 'Medium' || item.severity === 'High' || item.severity === 'Critical') {
      existing[item.severity] = item.count
    }

    map.set(item.date, existing)
  })

  return Array.from(map.values())
}

export function TrendChart({ data }: TrendChartProps) {
  const points = formatChartData(data)
  const legend = [
    { label: 'Low', className: 'bg-chart-2' },
    { label: 'Medium', className: 'bg-chart-4' },
    { label: 'High', className: 'bg-chart-1' },
    { label: 'Critical', className: 'bg-destructive' },
  ]

  return (
    <Card className="rounded-[32px] border-border/70 bg-card/92 shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
      <CardHeader className="flex flex-col gap-4 p-5 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <p className="text-xs uppercase tracking-[0.24em] text-muted-foreground">Trendline</p>
          <CardTitle className="mt-2 text-xl font-semibold tracking-tight">Open vulnerabilities over 12 months</CardTitle>
        </div>
        <div className="flex flex-wrap gap-2">
          {legend.map((item) => (
            <Badge key={item.label} variant="outline" className="rounded-full border-border/70 bg-background/30 px-2.5 py-1 text-xs text-foreground">
              <span className={`mr-2 inline-block size-2 rounded-full ${item.className}`} />
              {item.label}
            </Badge>
          ))}
        </div>
      </CardHeader>
      <CardContent className="pt-0">
        <div className="h-[320px] w-full">
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={points}>
            <CartesianGrid vertical={false} stroke="color-mix(in oklab, var(--border) 85%, transparent)" />
            <XAxis dataKey="date" axisLine={false} tickLine={false} tick={{ fill: 'var(--color-muted-foreground)', fontSize: 12 }} />
            <YAxis allowDecimals={false} axisLine={false} tickLine={false} tick={{ fill: 'var(--color-muted-foreground)', fontSize: 12 }} />
            <Tooltip
              contentStyle={{
                background: 'var(--color-popover)',
                border: '1px solid color-mix(in oklab, var(--border) 90%, transparent)',
                borderRadius: '16px',
                color: 'var(--color-popover-foreground)',
              }}
            />
            <Line type="monotone" dataKey="Low" stroke="var(--color-chart-2)" strokeWidth={2.5} dot={false} />
            <Line type="monotone" dataKey="Medium" stroke="var(--color-chart-4)" strokeWidth={2.5} dot={false} />
            <Line type="monotone" dataKey="High" stroke="var(--color-chart-1)" strokeWidth={2.5} dot={false} />
            <Line type="monotone" dataKey="Critical" stroke="var(--color-destructive)" strokeWidth={2.5} dot={false} />
          </LineChart>
        </ResponsiveContainer>
        </div>
      </CardContent>
    </Card>
  )
}
