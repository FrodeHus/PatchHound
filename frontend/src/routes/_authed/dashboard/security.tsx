import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchDashboardSummary, fetchSecurityManagerDashboardSummary } from '@/api/dashboard.functions'
import { SecurityManagerOverview } from '@/components/features/dashboard/SecurityManagerOverview'
import { useTenantScope } from '@/components/layout/tenant-scope'

export const Route = createFileRoute('/_authed/dashboard/security')({
  component: SecurityDashboardPage,
})

function SecurityDashboardPage() {
  const { selectedTenantId } = useTenantScope()

  const summaryQuery = useQuery({
    queryKey: ['dashboard', 'summary', selectedTenantId],
    queryFn: () => fetchDashboardSummary({ data: {} }),
    enabled: Boolean(selectedTenantId),
    staleTime: 30_000,
  })
  const managerSummaryQuery = useQuery({
    queryKey: ['dashboard', 'security-manager-summary', selectedTenantId],
    queryFn: () => fetchSecurityManagerDashboardSummary(),
    enabled: Boolean(selectedTenantId),
    staleTime: 30_000,
  })

  const summary = summaryQuery.data
  const managerSummary = managerSummaryQuery.data

  if (!summary || !managerSummary) {
    return null
  }

  return (
    <SecurityManagerOverview
      summary={summary}
      managerSummary={managerSummary}
      isLoading={summaryQuery.isFetching || managerSummaryQuery.isFetching}
    />
  )
}
