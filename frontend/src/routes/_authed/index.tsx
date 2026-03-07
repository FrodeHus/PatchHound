import { Link, createFileRoute } from '@tanstack/react-router'
import { AlertTriangle, CheckCircle2, Settings2, ShieldAlert, TimerReset, Users, UsersRound, Waypoints } from 'lucide-react'
import { fetchDashboardSummary, fetchDashboardTrends } from '@/api/dashboard.functions'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { CriticalVulnerabilities } from '@/components/features/dashboard/CriticalVulnerabilities'
import { ExposureScore } from '@/components/features/dashboard/ExposureScore'
import { RemediationVelocity } from '@/components/features/dashboard/RemediationVelocity'
import { SlaComplianceCard } from '@/components/features/dashboard/SlaComplianceCard'
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
  const quickLinks = [
    {
      label: 'Vulnerabilities',
      description: 'Review backlog, severity, and status.',
      to: '/vulnerabilities',
      icon: ShieldAlert,
    },
    {
      label: 'Assets',
      description: 'Inspect affected hosts and platforms.',
      to: '/assets',
      icon: Waypoints,
    },
    {
      label: 'Teams',
      description: 'Manage ownership and routing.',
      to: '/admin/teams',
      icon: UsersRound,
    },
    {
      label: 'Users',
      description: 'Control access and membership.',
      to: '/admin/users',
      icon: Users,
    },
    {
      label: 'Settings',
      description: 'Configure integrations and defaults.',
      to: '/settings',
      icon: Settings2,
    },
  ] as const
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
      <Card className="overflow-hidden rounded-[36px] border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_18%,transparent),transparent_42%),linear-gradient(180deg,color-mix(in_oklab,var(--card)_92%,black),var(--card))] shadow-[inset_0_1px_0_rgba(255,255,255,0.04)]">
        <CardContent className="p-6 sm:p-7">
          <div className="flex flex-col gap-5 xl:flex-row xl:items-end xl:justify-between">
            <div className="max-w-3xl">
              <Badge variant="outline" className="rounded-full border-primary/20 bg-primary/10 px-3 py-1 text-primary">
                Dashboard
              </Badge>
              <h2 className="mt-4 text-3xl font-semibold tracking-[-0.04em] text-foreground sm:text-4xl">
                Exposure posture, remediation flow, and critical queue in one operating view.
              </h2>
              <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
                Monitor what is drifting, where remediation is slowing, and which critical issues are aging into risk.
              </p>
              <div className="mt-5 grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                {quickLinks.map((item) => {
                  const Icon = item.icon
                  return (
                    <Link
                      key={item.label}
                      to={item.to}
                      className="group rounded-3xl border border-border/70 bg-background/35 p-4 transition-colors hover:border-primary/40 hover:bg-background/55"
                    >
                      <div className="flex items-start justify-between gap-3">
                        <div>
                          <p className="text-sm font-medium text-foreground">{item.label}</p>
                          <p className="mt-1 text-xs leading-5 text-muted-foreground">{item.description}</p>
                        </div>
                        <span className="rounded-2xl border border-border/80 bg-background/70 p-2 text-primary transition-transform group-hover:translate-x-0.5">
                          <Icon className="size-4" />
                        </span>
                      </div>
                    </Link>
                  )
                })}
              </div>
            </div>
            <div className="grid gap-3 sm:grid-cols-2 xl:w-[34rem]">
              {statCards.map((item) => {
                const Icon = item.icon
                return (
                  <div key={item.label} className="rounded-3xl border border-border/70 bg-background/30 p-4 backdrop-blur">
                    <div className="flex items-center justify-between">
                      <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{item.label}</p>
                      <Icon className={`size-4 ${item.tone}`} />
                    </div>
                    <p className="mt-3 text-3xl font-semibold tracking-[-0.04em]">{item.value}</p>
                  </div>
                )
              })}
            </div>
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-4 xl:grid-cols-3">
        <ExposureScore score={summary.exposureScore} />
        <SlaComplianceCard
          percent={summary.slaCompliancePercent}
          overdueCount={summary.overdueTaskCount}
          totalCount={summary.totalTaskCount}
        />
        <RemediationVelocity
          averageDays={summary.averageRemediationDays}
          vulnerabilitiesBySeverity={summary.vulnerabilitiesBySeverity}
        />
      </div>

      <div className="grid gap-4 xl:grid-cols-5">
        <div className="xl:col-span-3">
          <TrendChart data={trends} />
        </div>
        <div className="xl:col-span-2">
          <CriticalVulnerabilities items={summary.topCriticalVulnerabilities} />
        </div>
      </div>
    </section>
  )
}
