import { createFileRoute } from '@tanstack/react-router'
import {
  useDashboardSummary,
  useDashboardTrends,
} from '@/api/useDashboard'
import { CriticalVulnerabilities } from '@/components/features/dashboard/CriticalVulnerabilities'
import { ExposureScore } from '@/components/features/dashboard/ExposureScore'
import { RemediationVelocity } from '@/components/features/dashboard/RemediationVelocity'
import { SlaComplianceCard } from '@/components/features/dashboard/SlaComplianceCard'
import { TrendChart } from '@/components/features/dashboard/TrendChart'

export const Route = createFileRoute('/')({
  component: DashboardPage,
})

export function DashboardPage() {
  const summaryQuery = useDashboardSummary()
  const trendsQuery = useDashboardTrends()

  if (summaryQuery.isLoading || trendsQuery.isLoading) {
    return <p className="text-sm text-muted-foreground">Loading dashboard...</p>
  }

  if (summaryQuery.isError || trendsQuery.isError || !summaryQuery.data || !trendsQuery.data) {
    return <p className="text-sm text-destructive">Failed to load dashboard data.</p>
  }

  const summary = summaryQuery.data

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
          <TrendChart data={trendsQuery.data} />
        </div>
        <div className="xl:col-span-2">
          <CriticalVulnerabilities items={summary.topCriticalVulnerabilities} />
        </div>
      </div>
    </section>
  )
}
