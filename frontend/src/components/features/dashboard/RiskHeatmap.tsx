import { useMemo } from 'react'
import { Flame } from 'lucide-react'
import type { DashboardSummary } from '@/api/dashboard.schemas'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip'

type RiskHeatmapProps = {
  data: DashboardSummary['vulnerabilitiesByDeviceGroup']
  isLoading?: boolean
  onCellClick?: (deviceGroup: string, severity: string) => void
}

const severityColumns = ['Critical', 'High', 'Medium', 'Low'] as const
type SeverityKey = (typeof severityColumns)[number]

const severityDataKey: Record<SeverityKey, 'critical' | 'high' | 'medium' | 'low'> = {
  Critical: 'critical',
  High: 'high',
  Medium: 'medium',
  Low: 'low',
}

function intensityClass(value: number, max: number): string {
  if (value === 0) return 'bg-muted/40 text-muted-foreground/60'
  const ratio = max > 0 ? value / max : 0
  if (ratio >= 0.75) return 'bg-destructive/70 text-destructive-foreground'
  if (ratio >= 0.5) return 'bg-chart-1/50 text-foreground'
  if (ratio >= 0.25) return 'bg-chart-4/40 text-foreground'
  return 'bg-chart-2/30 text-foreground'
}

export function RiskHeatmap({ data, isLoading, onCellClick }: RiskHeatmapProps) {
  const globalMax = useMemo(
    () => Math.max(1, ...data.flatMap((g) => [g.critical, g.high, g.medium, g.low])),
    [data],
  )

  if (!data.length && !isLoading) return null

  return (
    <Card className="rounded-[32px] border-border/70 bg-card/92 shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
      <CardHeader className="flex flex-row items-start justify-between space-y-0 p-5 pb-0">
        <div>
          <p className="text-xs uppercase tracking-[0.24em] text-muted-foreground">Risk density</p>
          <CardTitle className="mt-2 text-xl font-semibold tracking-tight">Device group risk heatmap</CardTitle>
        </div>
        <div className="flex items-center gap-2">
          <div className="flex items-center gap-1.5">
            {['bg-muted/40', 'bg-chart-2/30', 'bg-chart-4/40', 'bg-chart-1/50', 'bg-destructive/70'].map((cls, i) => (
              <span key={i} className={`inline-block size-3 rounded ${cls}`} />
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
          <div className="h-[250px] w-full animate-pulse rounded-2xl bg-muted/60" />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full border-separate border-spacing-1.5">
              <thead>
                <tr>
                  <th className="px-3 py-2 text-left text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                    Device Group
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
                {data.map((group) => {
                  const total = group.critical + group.high + group.medium + group.low
                  return (
                    <tr key={group.deviceGroupName}>
                      <td className="max-w-[180px] truncate rounded-lg bg-muted/20 px-3 py-2.5 text-sm font-medium">
                        {group.deviceGroupName}
                      </td>
                      {severityColumns.map((sev) => {
                        const value = group[severityDataKey[sev]]
                        return (
                          <td key={sev} className="text-center">
                            <Tooltip>
                              <TooltipTrigger
                                render={
                                  <button
                                    type="button"
                                    className={`inline-flex min-w-[3.5rem] items-center justify-center rounded-lg px-3 py-2.5 text-sm font-semibold tabular-nums transition-transform ${intensityClass(value, globalMax)} ${onCellClick ? 'cursor-pointer hover:scale-105' : ''}`}
                                    onClick={() => onCellClick?.(group.deviceGroupName, sev)}
                                  />
                                }
                              >
                                {value}
                              </TooltipTrigger>
                              <TooltipContent>
                                {group.deviceGroupName}: {value} {sev.toLowerCase()} vulnerabilities
                              </TooltipContent>
                            </Tooltip>
                          </td>
                        )
                      })}
                      <td className="text-center">
                        <Badge variant="outline" className="rounded-full border-border/70 bg-background/30 text-foreground">
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
