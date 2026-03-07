import { createFileRoute } from '@tanstack/react-router'
import { fetchDashboardSummary, fetchDashboardTrends } from '@/api/dashboard.functions'
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

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Dashboard</h1>

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
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
