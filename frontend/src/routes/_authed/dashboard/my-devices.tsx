import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchOwnerDashboardSummary } from '@/api/dashboard.functions'
import { DeviceOwnerOverview } from '@/components/features/dashboard/DeviceOwnerOverview'
import { useTenantScope } from '@/components/layout/tenant-scope'

export const Route = createFileRoute('/_authed/dashboard/my-devices')({
  component: MyDevicesDashboardPage,
})

function MyDevicesDashboardPage() {
  const { selectedTenantId } = useTenantScope()

  const ownerSummaryQuery = useQuery({
    queryKey: ['dashboard', 'owner-summary', selectedTenantId],
    queryFn: () => fetchOwnerDashboardSummary(),
    enabled: Boolean(selectedTenantId),
    staleTime: 30_000,
  })

  return (
    <DeviceOwnerOverview
      summary={ownerSummaryQuery.data ?? {
        ownedAssetCount: 0,
        assetsNeedingAttention: 0,
        openActionCount: 0,
        overdueActionCount: 0,
        topOwnedAssets: [],
        actions: [],
        cloudAppActions: [],
      }}
      isLoading={ownerSummaryQuery.isPending || ownerSummaryQuery.isFetching}
    />
  )
}
