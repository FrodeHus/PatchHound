import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchTechnicalManagerDashboardSummary } from '@/api/dashboard.functions'
import { TechnicalManagerOverview } from '@/components/features/dashboard/TechnicalManagerOverview'
import { useTenantScope } from '@/components/layout/tenant-scope'

export const Route = createFileRoute('/_authed/dashboard/technical')({
  component: TechnicalDashboardPage,
})

function TechnicalDashboardPage() {
  const { selectedTenantId } = useTenantScope()

  const summaryQuery = useQuery({
    queryKey: ['dashboard', 'technical-manager-summary', selectedTenantId],
    queryFn: () => fetchTechnicalManagerDashboardSummary(),
    enabled: Boolean(selectedTenantId),
    staleTime: 30_000,
  })

  if (!summaryQuery.data) {
    return null
  }

  return (
    <TechnicalManagerOverview
      summary={summaryQuery.data}
      isLoading={summaryQuery.isFetching}
    />
  )
}
