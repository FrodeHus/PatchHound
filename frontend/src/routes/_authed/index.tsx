import { createFileRoute } from '@tanstack/react-router'
import { AlertTriangle, CheckCircle2, ShieldAlert, TimerReset } from 'lucide-react'
import { fetchDashboardSummary, fetchDashboardTrends } from '@/api/dashboard.functions'
import { Card, CardContent } from '@/components/ui/card'
import { InsetPanel } from '@/components/ui/inset-panel'
import { CriticalVulnerabilities } from '@/components/features/dashboard/CriticalVulnerabilities'
import { ExposureSlaCard } from '@/components/features/dashboard/ExposureSlaCard'
import { RemediationVelocity } from '@/components/features/dashboard/RemediationVelocity'
import { TrendChart } from '@/components/features/dashboard/TrendChart'

export const Route = createFileRoute('/_authed/')({
  loader: async () => {
    const [summary, trends] = await Promise.all([
      fetchDashboardSummary(),
      fetchDashboardTrends(),
    ])
    return { summary, trends }
  },
  component: DashboardPage,
})

function DashboardPage() {
  const { summary, trends } = Route.useLoaderData()
  const statCards = [
    {
      label: 'Critical backlog',
      value: summary.vulnerabilitiesBySeverity.Critical ?? 0,
      icon: ShieldAlert,
      tone: 'text-destructive',
    },
    {
      label: 'Overdue actions',
      value: summary.overdueTaskCount,
      icon: TimerReset,
      tone: 'text-primary',
    },
    {
      label: 'Healthy tasks',
      value: Math.max(summary.totalTaskCount - summary.overdueTaskCount, 0),
      icon: CheckCircle2,
      tone: 'text-chart-3',
    },
    {
      label: 'Open statuses',
      value: Object.values(summary.vulnerabilitiesByStatus).reduce((total, count) => total + count, 0),
      icon: AlertTriangle,
      tone: 'text-chart-2',
    },
  ]

  return (
    <section className="space-y-6 pb-4">
      <Card className="overflow-hidden rounded-2xl border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_18%,transparent),transparent_42%),linear-gradient(180deg,color-mix(in_oklab,var(--card)_92%,black),var(--card))] shadow-[inset_0_1px_0_rgba(255,255,255,0.04)]">
        <CardContent className="p-6 sm:p-7">
          <div className="grid gap-6 xl:grid-cols-[minmax(0,1.8fr)_minmax(22rem,1fr)]">
            <InsetPanel emphasis="subtle" className="p-5 backdrop-blur-sm">
              <TrendChart data={trends} embedded />
            </InsetPanel>
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-2">
              {statCards.map((item) => {
                const Icon = item.icon
                return (
                  <InsetPanel key={item.label} className="p-4 backdrop-blur-sm">
                    <div className="flex items-center justify-between">
                      <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{item.label}</p>
                      <Icon className={`size-4 ${item.tone}`} />
                    </div>
                    <p className="mt-3 text-3xl font-semibold tracking-[-0.04em]">{item.value}</p>
                  </InsetPanel>
                )
              })}
            </div>
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-4 xl:grid-cols-3">
        <div className="xl:col-span-2">
          <ExposureSlaCard
            exposureScore={summary.exposureScore}
            slaCompliancePercent={summary.slaCompliancePercent}
            overdueCount={summary.overdueTaskCount}
            totalCount={summary.totalTaskCount}
          />
        </div>
        <RemediationVelocity
          averageDays={summary.averageRemediationDays}
          vulnerabilitiesBySeverity={summary.vulnerabilitiesBySeverity}
        />
      </div>

      <div className="grid gap-4 xl:grid-cols-5">
        <div className="xl:col-span-5">
          <CriticalVulnerabilities items={summary.topCriticalVulnerabilities} summary={summary} />
        </div>
      </div>
    </section>
  )
}
