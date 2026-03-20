import { Skeleton } from '@/components/ui/skeleton'
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
import { TrendingDown } from 'lucide-react'
import type { BurndownTrend } from '@/api/dashboard.schemas'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

type BurndownChartProps = {
  data?: BurndownTrend
  isLoading?: boolean
}

function formatAxisDate(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return new Intl.DateTimeFormat('en', { month: 'short', day: 'numeric' }).format(date)
}

export function BurndownChart({ data, isLoading }: BurndownChartProps) {
  if (!data || data.items.length === 0) return null

  const items = data.items
  const totalDiscovered = items.reduce((sum, i) => sum + i.discovered, 0)
  const totalResolved = items.reduce((sum, i) => sum + i.resolved, 0)
  const netDirection = totalResolved >= totalDiscovered ? 'Burning down' : 'Backlog growing'

  return (
    <Card className="rounded-[32px] border-border/70 bg-card/92 shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
      <CardHeader className="flex flex-row items-start justify-between space-y-0 p-5 pb-0">
        <div>
          <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Burndown</p>
          <CardTitle className="mt-2 text-xl font-semibold tracking-tight">Discovered vs. resolved over 90 days</CardTitle>
        </div>
        <div className="flex items-center gap-2">
          <Badge variant="outline" className="rounded-full border-border/70 bg-background/30 text-foreground">
            {netDirection}
          </Badge>
          <span className="flex size-11 items-center justify-center rounded-2xl border border-chart-3/20 bg-chart-3/10 text-chart-3">
            <TrendingDown className="size-5" />
          </span>
        </div>
      </CardHeader>
      <CardContent className="pt-4">
        {isLoading ? (
          <Skeleton className="h-[300px] w-full " />
        ) : (
          <>
            <div className="h-[300px] w-full">
              <ResponsiveContainer width="100%" height="100%">
                <ComposedChart data={items}>
                  <CartesianGrid vertical={false} stroke="color-mix(in oklab, var(--border) 85%, transparent)" />
                  <XAxis
                    dataKey="date"
                    tickFormatter={formatAxisDate}
                    minTickGap={24}
                    axisLine={false}
                    tickLine={false}
                    tick={{ fill: 'var(--color-muted-foreground)', fontSize: 12 }}
                  />
                  <YAxis
                    allowDecimals={false}
                    axisLine={false}
                    tickLine={false}
                    tick={{ fill: 'var(--color-muted-foreground)', fontSize: 12 }}
                  />
                  <Tooltip
                    labelFormatter={(value) => formatAxisDate(String(value))}
                    contentStyle={{
                      background: 'var(--color-popover)',
                      border: '1px solid color-mix(in oklab, var(--border) 90%, transparent)',
                      borderRadius: '16px',
                      color: 'var(--color-popover-foreground)',
                    }}
                  />
                  <Area
                    type="monotone"
                    dataKey="discovered"
                    fill="var(--color-destructive)"
                    fillOpacity={0.08}
                    stroke="var(--color-destructive)"
                    strokeWidth={2}
                    dot={false}
                    name="Discovered"
                  />
                  <Area
                    type="monotone"
                    dataKey="resolved"
                    fill="var(--color-chart-3)"
                    fillOpacity={0.08}
                    stroke="var(--color-chart-3)"
                    strokeWidth={2}
                    dot={false}
                    name="Resolved"
                  />
                  <Line
                    type="monotone"
                    dataKey="netOpen"
                    stroke="var(--color-chart-4)"
                    strokeWidth={2}
                    strokeDasharray="6 3"
                    dot={false}
                    name="Net open"
                  />
                </ComposedChart>
              </ResponsiveContainer>
            </div>
            <div className="mt-3 flex flex-wrap gap-2">
              {[
                { label: 'Discovered', className: 'bg-destructive' },
                { label: 'Resolved', className: 'bg-chart-3' },
                { label: 'Net open', className: 'bg-chart-4' },
              ].map((item) => (
                <Badge key={item.label} variant="outline" className="rounded-full border-border/70 bg-background/30 px-2.5 py-1 text-xs text-foreground">
                  <span className={`mr-2 inline-block size-2 rounded-full ${item.className}`} />
                  {item.label}
                </Badge>
              ))}
            </div>
          </>
        )}
      </CardContent>
    </Card>
  )
}
