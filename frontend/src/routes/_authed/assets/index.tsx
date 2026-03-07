import { createFileRoute } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { fetchAssets } from '@/api/assets.functions'
import { assignAssetOwner, setAssetCriticality } from '@/api/assets.functions'
import { AssetManagementTable } from '@/components/features/assets/AssetManagementTable'

export const Route = createFileRoute('/_authed/assets/')({
  loader: () => fetchAssets({ data: {} }),
  component: AssetsPage,
})

function AssetsPage() {
  const data = Route.useLoaderData()
  const ownerMutation = useMutation({
    mutationFn: async (payload: { assetId: string; ownerType: 'User' | 'Team'; ownerId: string }) => {
      await assignAssetOwner({
        data: {
          assetId: payload.assetId,
          ownerType: payload.ownerType,
          ownerId: payload.ownerId,
        },
      })
    },
  })
  const criticalityMutation = useMutation({
    mutationFn: async (payload: { assetId: string; criticality: string }) => {
      await setAssetCriticality({
        data: {
          assetId: payload.assetId,
          criticality: payload.criticality,
        },
      })
    },
  })

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Assets</h1>
      <AssetManagementTable
        assets={data.items}
        totalCount={data.totalCount}
        isUpdating={ownerMutation.isPending || criticalityMutation.isPending}
        onAssignOwner={(assetId, ownerType, ownerId) => {
          ownerMutation.mutate({ assetId, ownerType, ownerId })
        }}
        onSetCriticality={(assetId, criticality) => {
          criticalityMutation.mutate({ assetId, criticality })
        }}
      />
    </section>
  )
}
