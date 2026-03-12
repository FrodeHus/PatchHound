import { useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { assignAssetOwner, assignAssetSecurityProfile, assignSoftwareCpeBinding, fetchAssetDetail, fetchAssets, setAssetCriticality } from '@/api/assets.functions'
import { fetchSecurityProfiles } from '@/api/security-profiles.functions'
import { AssetDetailPane } from '@/components/features/assets/AssetDetailPane'
import { AssetManagementTable } from '@/components/features/assets/AssetManagementTable'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { assetQueryKeys, buildAssetsListRequest } from '@/features/assets/list-state'
import { baseListSearchSchema, searchBooleanSchema, searchStringSchema } from '@/routes/-list-search'
import { createListSearchUpdater } from '@/routes/list-search-helpers'

const assetsSearchSchema = baseListSearchSchema.extend({
  search: searchStringSchema,
  assetType: searchStringSchema,
  criticality: searchStringSchema,
  ownerType: searchStringSchema,
  unassignedOnly: searchBooleanSchema,
})

export const Route = createFileRoute('/_authed/assets/')({
  validateSearch: assetsSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) => fetchAssets({ data: buildAssetsListRequest(deps) }),
  component: AssetsPage,
})

function AssetsPage() {
  const initialData = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId
  const searchActions = createListSearchUpdater<typeof search>(navigate)
  const queryClient = useQueryClient()
  const [selectedAssetId, setSelectedAssetId] = useState<string | null>(null)
  const assetsQuery = useQuery({
    queryKey: assetQueryKeys.list(selectedTenantId, search),
    queryFn: () => fetchAssets({ data: buildAssetsListRequest(search) }),
    initialData: canUseInitialData ? initialData : undefined,
  })
  const assets = assetsQuery.data ?? (canUseInitialData ? initialData : undefined)
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
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: assetQueryKeys.all })
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
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: assetQueryKeys.all })
    },
  })
  const securityProfileMutation = useMutation({
    mutationFn: async (payload: { assetId: string; securityProfileId: string | null }) => {
      await assignAssetSecurityProfile({ data: payload })
    },
    onSuccess: async () => {
      if (selectedAssetId) {
        await queryClient.invalidateQueries({ queryKey: assetQueryKeys.detail(selectedTenantId, selectedAssetId) })
      }
      await queryClient.invalidateQueries({ queryKey: assetQueryKeys.all })
    },
  })
  const softwareCpeBindingMutation = useMutation({
    mutationFn: async (payload: { assetId: string; cpe23Uri: string | null }) => {
      await assignSoftwareCpeBinding({ data: payload })
    },
    onSuccess: async () => {
      if (selectedAssetId) {
        await queryClient.invalidateQueries({ queryKey: assetQueryKeys.detail(selectedTenantId, selectedAssetId) })
      }
      await queryClient.invalidateQueries({ queryKey: assetQueryKeys.all })
    },
  })
  const assetDetailQuery = useQuery({
    queryKey: assetQueryKeys.detail(selectedTenantId, selectedAssetId),
    queryFn: () => fetchAssetDetail({ data: { assetId: selectedAssetId! } }),
    enabled: Boolean(selectedAssetId),
  })
  const securityProfilesQuery = useQuery({
    queryKey: ['security-profiles', selectedTenantId],
    queryFn: () => fetchSecurityProfiles({ data: {} }),
  })

  if (!assets) {
    return null
  }

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Assets</h1>
      <AssetManagementTable
        assets={assets.items}
        totalCount={assets.totalCount}
        isUpdating={ownerMutation.isPending || criticalityMutation.isPending || securityProfileMutation.isPending}
        selectedAssetId={selectedAssetId}
        searchValue={search.search}
        assetTypeFilter={search.assetType}
        criticalityFilter={search.criticality}
        ownerTypeFilter={search.ownerType}
        unassignedOnly={search.unassignedOnly}
        page={assets.page}
        pageSize={assets.pageSize}
        totalPages={assets.totalPages}
        onSearchChange={(searchValue) => {
          searchActions.updateField('search', searchValue)
          setSelectedAssetId(null)
        }}
        onAssetTypeFilterChange={(assetType) => {
          searchActions.updateField('assetType', assetType)
          setSelectedAssetId(null)
        }}
        onCriticalityFilterChange={(criticality) => {
          searchActions.updateField('criticality', criticality)
          setSelectedAssetId(null)
        }}
        onOwnerTypeFilterChange={(ownerType) => {
          searchActions.updateField('ownerType', ownerType)
          setSelectedAssetId(null)
        }}
        onUnassignedOnlyChange={(value) => {
          searchActions.updateField('unassignedOnly', value)
          setSelectedAssetId(null)
        }}
        onPageChange={(page) => {
          searchActions.updatePage(page)
        }}
        onPageSizeChange={(nextPageSize) => {
          searchActions.updatePageSize(nextPageSize)
          setSelectedAssetId(null)
        }}
        onClearFilters={() => {
          searchActions.updateFields({
            search: '',
            assetType: '',
            criticality: '',
            ownerType: '',
            unassignedOnly: false,
          })
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
        isAssigningSoftwareCpeBinding={softwareCpeBindingMutation.isPending}
        isOpen={selectedAssetId !== null}
        onAssignSecurityProfile={(assetId, securityProfileId) => {
          securityProfileMutation.mutate({ assetId, securityProfileId })
        }}
        onAssignSoftwareCpeBinding={(assetId, cpe23Uri) => {
          softwareCpeBindingMutation.mutate({ assetId, cpe23Uri })
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
