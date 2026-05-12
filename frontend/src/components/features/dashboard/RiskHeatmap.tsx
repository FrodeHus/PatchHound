import { Skeleton } from '@/components/ui/skeleton'
import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Flame } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import { fetchDashboardHeatmap } from '@/api/dashboard.functions'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'

type HeatmapGroupBy = 'deviceGroup' | 'platform' | 'vendor'

const groupByOptions: { value: HeatmapGroupBy; label: string; columnHeader: string }[] = [
  { value: 'deviceGroup', label: 'Device Group', columnHeader: 'Device Group' },
  { value: 'platform', label: 'Platform', columnHeader: 'Platform' },
  { value: 'vendor', label: 'Vendor', columnHeader: 'Vendor' },
]

type RiskHeatmapProps = {
  filters?: {
    minAgeDays?: number
    platform?: string
    deviceGroup?: string
  }
  onCellClick?: (group: string, severity: string) => void
}

const severityColumns = ['Critical', 'High', 'Medium', 'Low'] as const
type SeverityKey = (typeof severityColumns)[number]

const severityDataKey: Record<SeverityKey, 'critical' | 'high' | 'medium' | 'low'> = {
  Critical: 'critical',
  High: 'high',
  Medium: 'medium',
  Low: 'low',
}

const severityChipKey: Record<SeverityKey, string> = {
  Critical: 'crit',
  High: 'high',
  Medium: 'med',
  Low: 'low',
}

function logIntensity(value: number, max: number): number {
  if (!value || !max) return 0
  return Math.max(0.18, Math.min(1, Math.log10(value + 1) / Math.log10(max + 1)))
}

function intensityClass(value: number, max: number): string {
  if (value === 0) return 'bg-muted/50 text-muted-foreground/60'
  const ratio = max > 0 ? value / max : 0
  if (ratio >= 0.75) return 'bg-destructive/70 text-destructive-foreground'
  if (ratio >= 0.5) return 'bg-chart-1/50 text-foreground'
  if (ratio >= 0.25) return 'bg-chart-4/40 text-foreground'
  return 'bg-chart-2/30 text-foreground'
}

export function RiskHeatmap({ filters, onCellClick }: RiskHeatmapProps) {
  const [groupBy, setGroupBy] = useState<HeatmapGroupBy>('deviceGroup')

  const heatmapQuery = useQuery({
    queryKey: ['dashboard', 'heatmap', groupBy, filters?.minAgeDays, filters?.platform, filters?.deviceGroup],
    queryFn: () => fetchDashboardHeatmap({
      data: {
        groupBy,
        ...filters,
      },
    }),
    staleTime: 30_000,
  })

  const data = heatmapQuery.data
  const isLoading = heatmapQuery.isLoading

  const colMax = useMemo(() => {
    if (!data) return { critical: 1, high: 1, medium: 1, low: 1 }
    return {
      critical: Math.max(1, ...data.map((g) => g.critical)),
      high: Math.max(1, ...data.map((g) => g.high)),
      medium: Math.max(1, ...data.map((g) => g.medium)),
      low: Math.max(1, ...data.map((g) => g.low)),
    }
  }, [data])

  const activeOption = groupByOptions.find((o) => o.value === groupBy)!

  if ((!data || !data.length) && !isLoading) return null

  return (
    <Card className="rounded-[32px] border-border/70 bg-card/92 shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
      <CardHeader className="flex flex-row items-start justify-between space-y-0 p-5 pb-0">
        <div className="space-y-2">
          <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Risk density</p>
          <CardTitle className="text-xl font-semibold tracking-tight">Vulnerability heatmap</CardTitle>
          <ToggleGroup
            value={[groupBy]}
            onValueChange={(values) => {
              if (values.length > 0) setGroupBy(values[0] as HeatmapGroupBy)
            }}
            variant="outline"
            size="sm"
          >
            {groupByOptions.map((opt) => (
              <ToggleGroupItem key={opt.value} value={opt.value} className="text-xs">
                {opt.label}
              </ToggleGroupItem>
            ))}
          </ToggleGroup>
        </div>
        <div className="flex items-center gap-2">
          <div className="flex items-center gap-1.5">
            {[
              { cls: 'bg-muted/50', key: 'low-dim' },
              { cls: 'bg-chart-2/30', key: 'low' },
              { cls: 'bg-chart-4/40', key: 'med' },
              { cls: 'bg-chart-1/50', key: 'high' },
              { cls: 'bg-destructive/70', key: 'crit' },
            ].map(({ cls, key }) => (
              <span key={key} className={`inline-block size-3 rounded ${cls}`} />
            ))}
            <span className="ml-1 text-[11px] text-muted-foreground">Low → High</span>
          </div>
          <span className="flex size-11 items-center justify-center rounded-2xl border border-destructive/20 bg-destructive/10 text-destructive">
            <Flame className="size-5" />
          </span>
        </div>
      </CardHeader>
      <CardContent className="pt-4">
        {isLoading ? (
          <Skeleton className="h-[250px] w-full " />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full border-separate border-spacing-1.5">
              <thead>
                <tr>
                  <th className="px-3 py-2 text-left text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                    {activeOption.columnHeader}
                  </th>
                  {severityColumns.map((sev) => (
                    <th key={sev} className="px-3 py-2 text-center text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                      {sev}
                    </th>
                  ))}
                  <th className="px-3 py-2 text-center text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                    Total
                  </th>
                </tr>
              </thead>
              <tbody>
                {data?.map((row) => {
                  const total = row.critical + row.high + row.medium + row.low
                  return (
                    <tr key={row.label}>
                      <td className="max-w-[180px] truncate rounded-lg bg-muted/20 px-3 py-2.5 text-sm font-medium">
                        {row.label}
                      </td>
                      {severityColumns.map((sev) => {
                        const dataKey = severityDataKey[sev]
                        const value = row[dataKey]
                        const chipKey = severityChipKey[sev]
                        const intensity = logIntensity(value, colMax[dataKey])
                        return (
                          <td key={sev} className="text-center">
                            <Tooltip>
                              <TooltipTrigger
                                render={
                                  <button
                                    type="button"
                                    data-hm-chip={chipKey}
                                    style={{ '--i': intensity } as React.CSSProperties}
                                    className={`inline-flex min-w-[3.5rem] items-center justify-center rounded-full px-3 py-2.5 text-sm font-semibold tabular-nums transition-transform ${intensityClass(value, colMax[dataKey])} ${onCellClick ? 'cursor-pointer hover:scale-105' : ''}`}
                                    onClick={() => onCellClick?.(row.label, sev)}
                                  />
                                }
                              >
                                {value}
                              </TooltipTrigger>
                              <TooltipContent>
                                {row.label}: {value} {sev.toLowerCase()} vulnerabilities
                              </TooltipContent>
                            </Tooltip>
                          </td>
                        )
                      })}
                      <td className="text-center">
                        <Badge
                          variant="outline"
                          data-hm-chip="tot"
                          className="rounded-full border-border/70 bg-background/30 text-foreground"
                        >
                          {total}
                        </Badge>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </CardContent>
    </Card>
  )
}
