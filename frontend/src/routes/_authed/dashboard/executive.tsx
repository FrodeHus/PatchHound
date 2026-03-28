import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchDashboardSummary, fetchDashboardTrends } from '@/api/dashboard.functions'
import { CisoExecutiveOverview } from '@/components/features/dashboard/CisoExecutiveOverview'
import { useTenantScope } from '@/components/layout/tenant-scope'

export const Route = createFileRoute('/_authed/dashboard/executive')({
  component: ExecutiveDashboardPage,
})

function ExecutiveDashboardPage() {
  const { selectedTenantId } = useTenantScope()

  const summaryQuery = useQuery({
    queryKey: ['dashboard', 'summary', selectedTenantId],
    queryFn: () => fetchDashboardSummary({ data: {} }),
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
  const trends = trendsQuery.data

  if (!summary || !trends) {
    return null
  }

  return (
    <CisoExecutiveOverview
      summary={summary}
      trends={trends}
      isLoading={summaryQuery.isFetching || trendsQuery.isFetching}
      filters={{}}
    />
  )
}
