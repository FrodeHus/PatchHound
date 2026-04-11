import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchOwnerAssetsNeedingAttention } from '@/api/dashboard.functions'
import { DeviceOwnerAttentionView } from '@/components/features/dashboard/DeviceOwnerAttentionView'
import { useTenantScope } from '@/components/layout/tenant-scope'

export const Route = createFileRoute('/_authed/dashboard/my-devices/attention')({
  component: MyDevicesAttentionPage,
})

function MyDevicesAttentionPage() {
  const { selectedTenantId } = useTenantScope()

  const attentionQuery = useQuery({
    queryKey: ['dashboard', 'owner-devices-needing-attention', selectedTenantId],
    queryFn: () => fetchOwnerAssetsNeedingAttention(),
    enabled: Boolean(selectedTenantId),
    staleTime: 30_000,
  })

  return (
    <DeviceOwnerAttentionView
      items={attentionQuery.data ?? []}
      isLoading={attentionQuery.isPending || attentionQuery.isFetching}
    />
  )
}
