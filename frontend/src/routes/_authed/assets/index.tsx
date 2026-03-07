import { useState } from 'react'
import { createFileRoute, useRouter } from '@tanstack/react-router'
import { useMutation, useQuery } from '@tanstack/react-query'
import { assignAssetOwner, fetchAssetDetail, fetchAssets, setAssetCriticality } from '@/api/assets.functions'
import { AssetDetailPane } from '@/components/features/assets/AssetDetailPane'
import { AssetManagementTable } from '@/components/features/assets/AssetManagementTable'

export const Route = createFileRoute('/_authed/assets/')({
  loader: () => fetchAssets({ data: {} }),
  component: AssetsPage,
})

function AssetsPage() {
  const initialData = Route.useLoaderData()
  const router = useRouter()
  const [selectedAssetId, setSelectedAssetId] = useState<string | null>(null)
  const [assetTypeFilter, setAssetTypeFilter] = useState('')
  const assetsQuery = useQuery({
    queryKey: ['assets', assetTypeFilter],
    queryFn: () => fetchAssets({ data: assetTypeFilter ? { assetType: assetTypeFilter } : {} }),
    initialData,
  })
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
    onSuccess: () => { void router.invalidate() },
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
    onSuccess: () => { void router.invalidate() },
  })
  const assetDetailQuery = useQuery({
    queryKey: ['asset', selectedAssetId],
    queryFn: () => fetchAssetDetail({ data: { assetId: selectedAssetId! } }),
    enabled: Boolean(selectedAssetId),
  })

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Assets</h1>
      <AssetManagementTable
        assets={assetsQuery.data.items}
        totalCount={assetsQuery.data.totalCount}
        isUpdating={ownerMutation.isPending || criticalityMutation.isPending}
        selectedAssetId={selectedAssetId}
        assetTypeFilter={assetTypeFilter}
        onAssetTypeFilterChange={(assetType) => {
          setAssetTypeFilter(assetType)
          setSelectedAssetId(null)
        }}
        onSelectAsset={setSelectedAssetId}
        onAssignOwner={(assetId, ownerType, ownerId) => {
          ownerMutation.mutate({ assetId, ownerType, ownerId })
        }}
        onSetCriticality={(assetId, criticality) => {
          criticalityMutation.mutate({ assetId, criticality })
        }}
      />
      <AssetDetailPane
        asset={assetDetailQuery.data ?? null}
        isLoading={assetDetailQuery.isLoading}
        isOpen={selectedAssetId !== null}
        onOpenChange={(open) => {
          if (!open) {
            setSelectedAssetId(null)
          }
        }}
      />
    </section>
  )
}
