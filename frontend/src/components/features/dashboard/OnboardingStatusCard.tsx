import { Skeleton } from '@/components/ui/skeleton'
import { Link } from '@tanstack/react-router'
import { MonitorCheck } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'

type OnboardingStatusCardProps = {
  onboardingBreakdown: Record<string, number>
  isLoading?: boolean
}

export function OnboardingStatusCard({ onboardingBreakdown, isLoading }: OnboardingStatusCardProps) {
  const entries = Object.entries(onboardingBreakdown).sort(([a], [b]) => {
    if (a === 'Onboarded') return -1
    if (b === 'Onboarded') return 1
    return a.localeCompare(b)
  })
  const total = entries.reduce((sum, [, count]) => sum + count, 0)
  const onboarded = onboardingBreakdown['Onboarded'] ?? 0
  const nonOnboarded = total - onboarded

  return (
    <Card className="rounded-[32px] border-border/70 bg-card/92 shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
      <CardContent className="p-5">
        {isLoading ? (
          <div className="space-y-3">
            <Skeleton className="h-4 w-32 rounded bg-muted/60" />
            <Skeleton className="h-10 w-20 rounded bg-muted/60" />
            <Skeleton className="h-4 w-48 rounded bg-muted/60" />
          </div>
        ) : (
          <>
            <div className="flex items-start justify-between gap-4">
              <div>
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Onboarding status</p>
                <p className="mt-3 text-4xl font-semibold tracking-[-0.04em]">
                  {onboarded}<span className="text-lg text-muted-foreground">/{total}</span>
                </p>
                <p className="mt-1 text-xs text-muted-foreground">onboarded devices</p>
              </div>
              <span className="flex size-12 items-center justify-center rounded-2xl border border-chart-1/20 bg-chart-1/10 text-chart-1">
                <MonitorCheck className="size-5" />
              </span>
            </div>

            {nonOnboarded > 0 && (
              <div className="mt-4 space-y-1.5">
                {entries
                  .filter(([status]) => status !== 'Onboarded')
                  .map(([status, count]) => (
                    <Link
                      key={status}
                      to="/devices"
                      search={{
                        search: '',
                        criticality: '',
                        businessLabelId: '',
                        ownerType: '',
                        deviceGroup: '',
                        healthStatus: '',
                        onboardingStatus: status,
                        riskBand: '',
                        tag: '',
                        unassignedOnly: false,
                        page: 1,
                        pageSize: 25,
                      }}
                      className="flex items-center justify-between rounded-xl border border-border/70 bg-background/30 px-3 py-2 text-sm transition hover:bg-background/50"
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
