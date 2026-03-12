import { createFileRoute } from '@tanstack/react-router'
import { fetchDashboardRiskChanges } from '@/api/dashboard.functions'
import { RiskChangeWorkbench } from '@/components/features/vulnerabilities/RiskChangeWorkbench'

export const Route = createFileRoute('/_authed/vulnerabilities/changes')({
  loader: () => fetchDashboardRiskChanges(),
  component: VulnerabilityChangesPage,
})

function VulnerabilityChangesPage() {
  const brief = Route.useLoaderData()

  return (
    <section className="space-y-6">
      <RiskChangeWorkbench brief={brief} />
    </section>
  )
}
