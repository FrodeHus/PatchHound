import { Skeleton } from '@/components/ui/skeleton'
import { useMemo } from 'react'
import {
  Area,
  CartesianGrid,
  ComposedChart,
  Line,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { TrendData } from '@/api/dashboard.schemas'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

type TrendChartProps = {
  data: TrendData
  embedded?: boolean
  isLoading?: boolean
  onSeverityClick?: (severity: string) => void
}

type ChartPoint = {
  date: string
  Low: number
  Medium: number
  High: number
  Critical: number
  NetChange: number
}

function formatChartData(data: TrendData): ChartPoint[] {
  const map = new Map<string, Omit<ChartPoint, 'NetChange'>>()

  for (const item of data.items) {
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
  }

  const raw = Array.from(map.values())

  return raw.map((point, index) => {
    const total = point.Low + point.Medium + point.High + point.Critical
    const prevTotal = index > 0
      ? raw[index - 1].Low + raw[index - 1].Medium + raw[index - 1].High + raw[index - 1].Critical
      : total
    return { ...point, NetChange: total - prevTotal }
  })
}

function formatAxisDate(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return new Intl.DateTimeFormat('en', { month: 'short', day: 'numeric' }).format(date)
}

const severities = [
  { key: 'Critical', stroke: 'var(--color-destructive)', fill: 'var(--color-destructive)', dot: 'bg-destructive' },
  { key: 'High', stroke: 'var(--color-chart-1)', fill: 'var(--color-chart-1)', dot: 'bg-chart-1' },
  { key: 'Medium', stroke: 'var(--color-chart-4)', fill: 'var(--color-chart-4)', dot: 'bg-chart-4' },
  { key: 'Low', stroke: 'var(--color-chart-2)', fill: 'var(--color-chart-2)', dot: 'bg-chart-2' },
] as const

export function TrendChart({ data, embedded = false, isLoading, onSeverityClick }: TrendChartProps) {
  const points = useMemo(() => formatChartData(data), [data])

  const legend = [
    ...severities.map((s) => ({ label: s.key, className: s.dot })),
    { label: 'Net change', className: 'bg-foreground' },
  ]

  const header = (
    <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
      <div>
        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Trendline</p>
        <CardTitle className="mt-2 text-xl font-semibold tracking-tight">Open vulnerability trend over 90 days</CardTitle>
      </div>
      <div className="flex flex-wrap gap-2">
        {legend.map((item) => (
          <Badge
            key={item.label}
            variant="outline"
            className={`rounded-full border-border/70 bg-background/30 px-2.5 py-1 text-xs text-foreground ${onSeverityClick && item.label !== 'Net change' ? 'cursor-pointer hover:bg-accent/40' : ''}`}
            onClick={() => {
              if (onSeverityClick && item.label !== 'Net change') onSeverityClick(item.label)
            }}
          >
            <span className={`mr-2 inline-block size-2 rounded-full ${item.className}`} />
            {item.label}
          </Badge>
        ))}
      </div>
    </div>
  )

  const chart = isLoading ? (
    <Skeleton className="h-[320px] w-full " />
  ) : (
    <div className={embedded ? 'mt-5 h-[320px] w-full' : 'h-[320px] w-full'}>
      <ResponsiveContainer width="100%" height="100%">
        <ComposedChart data={points}>
          <CartesianGrid vertical={false} stroke="color-mix(in oklab, var(--border) 85%, transparent)" />
          <XAxis
            dataKey="date"
            tickFormatter={formatAxisDate}
            minTickGap={24}
            axisLine={false}
            tickLine={false}
            tick={{ fill: 'var(--color-muted-foreground)', fontSize: 12 }}
          />
          <YAxis allowDecimals={false} axisLine={false} tickLine={false} tick={{ fill: 'var(--color-muted-foreground)', fontSize: 12 }} />
          <Tooltip
            labelFormatter={(value) => formatAxisDate(String(value))}
            contentStyle={{
              background: 'var(--color-popover)',
              border: '1px solid color-mix(in oklab, var(--border) 90%, transparent)',
              borderRadius: '16px',
              color: 'var(--color-popover-foreground)',
            }}
          />
          {severities.map((s) => (
            <Area
              key={s.key}
              type="monotone"
              dataKey={s.key}
              stroke={s.stroke}
              fill={s.fill}
              fillOpacity={0.06}
              strokeWidth={2}
              dot={false}
              style={onSeverityClick ? { cursor: 'pointer' } : undefined}
              onClick={() => onSeverityClick?.(s.key)}
            />
          ))}
          <Line
            type="monotone"
            dataKey="NetChange"
            stroke="var(--color-foreground)"
            strokeWidth={1.5}
            strokeDasharray="5 3"
            strokeOpacity={0.5}
            dot={false}
            name="Net change"
          />
        </ComposedChart>
      </ResponsiveContainer>
    </div>
  )

  if (embedded) {
    return (
      <>
        {header}
        {chart}
      </>
    )
  }

  return (
    <Card className="rounded-[32px] border-border/70 bg-card/92 shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
      <CardHeader className="p-5 pb-4">{header}</CardHeader>
      <CardContent className="pt-0">{chart}</CardContent>
    </Card>
  )
}
