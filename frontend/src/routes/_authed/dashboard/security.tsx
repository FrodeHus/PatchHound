import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchDashboardSummary, fetchDashboardTrends, fetchSecurityManagerDashboardSummary } from '@/api/dashboard.functions'
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
  const trendsQuery = useQuery({
    queryKey: ['dashboard', 'trends', selectedTenantId],
    queryFn: () => fetchDashboardTrends({ data: {} }),
    enabled: Boolean(selectedTenantId),
    staleTime: 30_000,
  })

  const summary = summaryQuery.data
  const managerSummary = managerSummaryQuery.data
  const trends = trendsQuery.data

  if (!summary || !managerSummary || !trends) {
    return null
  }

  return (
    <SecurityManagerOverview
      summary={summary}
      managerSummary={managerSummary}
      trends={trends}
      isLoading={summaryQuery.isFetching || managerSummaryQuery.isFetching || trendsQuery.isFetching}
    />
  )
}
