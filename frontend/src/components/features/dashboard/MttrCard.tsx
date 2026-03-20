import { Skeleton } from '@/components/ui/skeleton'
import { ArrowDown, ArrowUp, Timer } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { InsetPanel } from '@/components/ui/inset-panel'

type MttrEntry = {
  severity: string
  days: number
  previousDays: number | null
}

type MttrCardProps = {
  data?: MttrEntry[]
  isLoading?: boolean
}

const severityOrder = ['Critical', 'High', 'Medium', 'Low']

function severityColor(severity: string): string {
  switch (severity) {
    case 'Critical': return 'text-destructive'
    case 'High': return 'text-chart-1'
    case 'Medium': return 'text-chart-4'
    case 'Low': return 'text-chart-2'
    default: return 'text-muted-foreground'
  }
}

function severityDotColor(severity: string): string {
  switch (severity) {
    case 'Critical': return 'bg-destructive'
    case 'High': return 'bg-chart-1'
    case 'Medium': return 'bg-chart-4'
    case 'Low': return 'bg-chart-2'
    default: return 'bg-muted-foreground'
  }
}

export function MttrCard({ data, isLoading }: MttrCardProps) {
  if (!data || data.length === 0) return null

  const sorted = [...data].sort(
    (a, b) => severityOrder.indexOf(a.severity) - severityOrder.indexOf(b.severity),
  )

  return (
    <Card className="rounded-[32px] border-border/70 bg-card/92 shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
      <CardHeader className="flex flex-row items-start justify-between space-y-0 p-5 pb-0">
        <div>
          <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Remediation speed</p>
          <CardTitle className="mt-2 text-xl font-semibold tracking-tight">Mean time to remediate</CardTitle>
        </div>
        <span className="flex size-11 items-center justify-center rounded-2xl border border-primary/20 bg-primary/12 text-primary">
          <Timer className="size-5" />
        </span>
      </CardHeader>
      <CardContent className="pt-4">
        {isLoading ? (
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-28 rounded-xl" />
            ))}
          </div>
        ) : (
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            {sorted.map((entry) => {
              const delta = entry.previousDays !== null ? entry.days - entry.previousDays : null
              const improved = delta !== null && delta < 0
              const worsened = delta !== null && delta > 0

              return (
                <InsetPanel key={entry.severity} className="p-4">
                  <div className="flex items-center gap-2">
                    <span className={`size-2.5 rounded-full ${severityDotColor(entry.severity)}`} />
                    <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{entry.severity}</p>
                  </div>
                  <p className={`mt-3 text-3xl font-semibold tracking-[-0.04em] ${severityColor(entry.severity)}`}>
                    {entry.days.toFixed(1)}
                    <span className="ml-1 text-base font-normal text-muted-foreground">days</span>
                  </p>
                  {delta !== null ? (
                    <div className="mt-2 flex items-center gap-1 text-xs">
                      {improved ? (
                        <ArrowDown className="size-3.5 text-tone-success-foreground" />
                      ) : worsened ? (
                        <ArrowUp className="size-3.5 text-tone-danger-foreground" />
                      ) : null}
                      <span className={improved ? 'text-tone-success-foreground' : worsened ? 'text-tone-danger-foreground' : 'text-muted-foreground'}>
                        {improved ? '' : worsened ? '+' : ''}{delta.toFixed(1)}d vs prior period
                      </span>
                    </div>
                  ) : (
                    <p className="mt-2 text-xs text-muted-foreground">No prior data</p>
                  )}
                </InsetPanel>
              )
            })}
          </div>
        )}
      </CardContent>
    </Card>
  )
}
