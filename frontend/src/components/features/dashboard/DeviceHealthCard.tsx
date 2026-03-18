import { Link } from '@tanstack/react-router'
import { HeartPulse } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'

type DeviceHealthCardProps = {
  healthBreakdown: Record<string, number>
  isLoading?: boolean
}

export function DeviceHealthCard({ healthBreakdown, isLoading }: DeviceHealthCardProps) {
  const entries = Object.entries(healthBreakdown).sort(([a], [b]) => {
    if (a === 'Active') return -1
    if (b === 'Active') return 1
    return a.localeCompare(b)
  })
  const total = entries.reduce((sum, [, count]) => sum + count, 0)
  const healthy = healthBreakdown['Active'] ?? 0
  const unhealthy = total - healthy

  return (
    <Card className="rounded-[32px] border-border/70 bg-card/92 shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
      <CardContent className="p-5">
        {isLoading ? (
          <div className="space-y-3">
            <div className="h-4 w-32 animate-pulse rounded bg-muted/60" />
            <div className="h-10 w-20 animate-pulse rounded bg-muted/60" />
            <div className="h-4 w-48 animate-pulse rounded bg-muted/60" />
          </div>
        ) : (
          <>
            <div className="flex items-start justify-between gap-4">
              <div>
                <p className="text-xs uppercase tracking-[0.24em] text-muted-foreground">Device health</p>
                <p className="mt-3 text-4xl font-semibold tracking-[-0.04em]">
                  {healthy}<span className="text-lg text-muted-foreground">/{total}</span>
                </p>
                <p className="mt-1 text-xs text-muted-foreground">healthy devices</p>
              </div>
              <span className="flex size-12 items-center justify-center rounded-2xl border border-chart-3/20 bg-chart-3/10 text-chart-3">
                <HeartPulse className="size-5" />
              </span>
            </div>

            {unhealthy > 0 && (
              <div className="mt-4 space-y-1.5">
                {entries
                  .filter(([status]) => status !== 'Active')
                  .map(([status, count]) => (
                    <Link
                      key={status}
                      to="/assets"
                      search={{
                        search: '',
                        assetType: 'Device',
                        criticality: '',
                        ownerType: '',
                        deviceGroup: '',
                        healthStatus: status,
                        riskScore: '',
                        exposureLevel: '',
                        tag: '',
                        unassignedOnly: false,
                        page: 1,
                        pageSize: 25,
                      }}
                      className="flex items-center justify-between rounded-xl border border-border/70 bg-background/35 px-3 py-2 text-sm transition hover:bg-background/60"
                    >
                      <span>{status}</span>
                      <Badge variant="outline" className="rounded-full border-border/70 text-xs">{count}</Badge>
                    </Link>
                  ))}
              </div>
            )}
          </>
        )}
      </CardContent>
    </Card>
  )
}
