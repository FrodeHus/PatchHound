import { useState } from 'react'
import { createFileRoute, useRouter } from '@tanstack/react-router'
import { useMutation, useQuery } from '@tanstack/react-query'
import { assignAssetOwner, assignAssetSecurityProfile, fetchAssetDetail, fetchAssets, setAssetCriticality } from '@/api/assets.functions'
import { fetchSecurityProfiles } from '@/api/security-profiles.functions'
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
  const securityProfileMutation = useMutation({
    mutationFn: async (payload: { assetId: string; securityProfileId: string | null }) => {
      await assignAssetSecurityProfile({ data: payload })
    },
    onSuccess: async () => {
      await router.invalidate()
      if (selectedAssetId) {
        await assetDetailQuery.refetch()
      }
    },
  })
  const assetDetailQuery = useQuery({
    queryKey: ['asset', selectedAssetId],
    queryFn: () => fetchAssetDetail({ data: { assetId: selectedAssetId! } }),
    enabled: Boolean(selectedAssetId),
  })
  const securityProfilesQuery = useQuery({
    queryKey: ['security-profiles'],
    queryFn: () => fetchSecurityProfiles({ data: {} }),
  })

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Assets</h1>
      <AssetManagementTable
        assets={assetsQuery.data.items}
        totalCount={assetsQuery.data.totalCount}
        isUpdating={ownerMutation.isPending || criticalityMutation.isPending || securityProfileMutation.isPending}
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
        securityProfiles={securityProfilesQuery.data?.items ?? []}
        isLoading={assetDetailQuery.isLoading}
        isAssigningSecurityProfile={securityProfileMutation.isPending}
        isOpen={selectedAssetId !== null}
        onAssignSecurityProfile={(assetId, securityProfileId) => {
          securityProfileMutation.mutate({ assetId, securityProfileId })
        }}
        onOpenChange={(open) => {
          if (!open) {
            setSelectedAssetId(null)
          }
        }}
      />
    </section>
  )
}
