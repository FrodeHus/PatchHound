import { useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { assignAssetOwner, assignAssetSecurityProfile, fetchAssetDetail, fetchAssets, setAssetCriticality } from '@/api/assets.functions'
import { AssetDetailPane } from '@/components/features/assets/AssetDetailPane'
import { AssetManagementTable } from '@/components/features/assets/AssetManagementTable'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { assetQueryKeys, buildAssetsListRequest, type AssetsListSearch } from '@/features/assets/list-state'
import { baseListSearchSchema, searchBooleanSchema, searchStringSchema } from '@/routes/-list-search'
import { createListSearchUpdater } from '@/routes/-list-search-helpers'

const devicesSearchSchema = baseListSearchSchema.extend({
  search: searchStringSchema,
  criticality: searchStringSchema,
  ownerType: searchStringSchema,
  deviceGroup: searchStringSchema,
  healthStatus: searchStringSchema,
  onboardingStatus: searchStringSchema,
  riskScore: searchStringSchema,
  exposureLevel: searchStringSchema,
  tag: searchStringSchema,
  unassignedOnly: searchBooleanSchema,
})

type DevicesSearch = {
  search: string
  criticality: string
  ownerType: string
  deviceGroup: string
  healthStatus: string
  onboardingStatus: string
  riskScore: string
  exposureLevel: string
  tag: string
  unassignedOnly: boolean
  page: number
  pageSize: number
}

function toDeviceAssetSearch(search: DevicesSearch): AssetsListSearch {
  return {
    ...search,
    assetType: 'Device',
  }
}

function buildDeviceListRequest(search: DevicesSearch) {
  return buildAssetsListRequest(toDeviceAssetSearch(search))
}

export const Route = createFileRoute('/_authed/devices/')({
  validateSearch: devicesSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: ({ deps }) => fetchAssets({ data: buildDeviceListRequest(deps) }),
  component: DevicesPage,
})

function DevicesPage() {
  const initialData = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId
  const searchActions = createListSearchUpdater<typeof search>(navigate)
  const queryClient = useQueryClient()
  const [selectedAssetId, setSelectedAssetId] = useState<string | null>(null)
  const deviceSearch = search as DevicesSearch
  const assetsQuery = useQuery({
    queryKey: assetQueryKeys.list(selectedTenantId, toDeviceAssetSearch(deviceSearch)),
    queryFn: () => fetchAssets({ data: buildDeviceListRequest(deviceSearch) }),
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
      toast.success('Owner assigned')
      await queryClient.invalidateQueries({ queryKey: assetQueryKeys.all })
    },
    onError: () => {
      toast.error('Failed to assign owner')
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
      toast.success('Criticality updated')
      await queryClient.invalidateQueries({ queryKey: assetQueryKeys.all })
    },
    onError: () => {
      toast.error('Failed to update criticality')
    },
  })
  const securityProfileMutation = useMutation({
    mutationFn: async (payload: { assetId: string; securityProfileId: string | null }) => {
      await assignAssetSecurityProfile({ data: payload })
    },
    onSuccess: async () => {
      toast.success('Security profile assigned')
      if (selectedAssetId) {
        await queryClient.invalidateQueries({ queryKey: assetQueryKeys.detail(selectedTenantId, selectedAssetId) })
      }
      await queryClient.invalidateQueries({ queryKey: assetQueryKeys.all })
    },
    onError: () => {
      toast.error('Failed to assign security profile')
    },
  })
  const assetDetailQuery = useQuery({
    queryKey: assetQueryKeys.detail(selectedTenantId, selectedAssetId),
    queryFn: () => fetchAssetDetail({ data: { assetId: selectedAssetId! } }),
    enabled: Boolean(selectedAssetId),
  })

  if (!assets) {
    return null
  }

  return (
    <section className="space-y-4">
      <AssetManagementTable
        title="Devices"
        description="Work the endpoint fleet with device-centric filters, then open the inspector from the device name."
        searchPlaceholder="Search devices"
        showAssetTypeFilter={false}
        showAssetTypeColumn={false}
        assets={assets.items}
        totalCount={assets.totalCount}
        isUpdating={ownerMutation.isPending || criticalityMutation.isPending || securityProfileMutation.isPending}
        selectedAssetId={selectedAssetId}
        searchValue={deviceSearch.search}
        assetTypeFilter="Device"
        criticalityFilter={deviceSearch.criticality}
        ownerTypeFilter={deviceSearch.ownerType}
        deviceGroupFilter={deviceSearch.deviceGroup}
        healthStatusFilter={deviceSearch.healthStatus}
        onboardingStatusFilter={deviceSearch.onboardingStatus}
        riskScoreFilter={deviceSearch.riskScore}
        exposureLevelFilter={deviceSearch.exposureLevel}
        tagFilter={deviceSearch.tag}
        unassignedOnly={deviceSearch.unassignedOnly}
        page={assets.page}
        pageSize={assets.pageSize}
        totalPages={assets.totalPages}
        onSearchChange={(searchValue) => {
          searchActions.updateField('search', searchValue)
          setSelectedAssetId(null)
        }}
        onAssetTypeFilterChange={() => {}}
        onCriticalityFilterChange={(criticality) => {
          searchActions.updateField('criticality', criticality)
          setSelectedAssetId(null)
        }}
        onOwnerTypeFilterChange={(ownerType) => {
          searchActions.updateField('ownerType', ownerType)
          setSelectedAssetId(null)
        }}
        onDeviceGroupFilterChange={(deviceGroup) => {
          searchActions.updateField('deviceGroup', deviceGroup)
          setSelectedAssetId(null)
        }}
        onHealthStatusFilterChange={(healthStatus) => {
          searchActions.updateField('healthStatus', healthStatus)
          setSelectedAssetId(null)
        }}
        onOnboardingStatusFilterChange={(onboardingStatus) => {
          searchActions.updateField('onboardingStatus', onboardingStatus)
          setSelectedAssetId(null)
        }}
        onRiskScoreFilterChange={(riskScore) => {
          searchActions.updateField('riskScore', riskScore)
          setSelectedAssetId(null)
        }}
        onExposureLevelFilterChange={(exposureLevel) => {
          searchActions.updateField('exposureLevel', exposureLevel)
          setSelectedAssetId(null)
        }}
        onTagFilterChange={(tag) => {
          searchActions.updateField('tag', tag)
          setSelectedAssetId(null)
        }}
        onUnassignedOnlyChange={(value) => {
          searchActions.updateField('unassignedOnly', value)
          setSelectedAssetId(null)
        }}
        onApplyStructuredFilters={(filters) => {
          searchActions.updateFields({
            criticality: filters.criticality,
            ownerType: filters.ownerType,
            deviceGroup: filters.deviceGroup,
            healthStatus: filters.healthStatus,
            onboardingStatus: filters.onboardingStatus,
            riskScore: filters.riskScore,
            exposureLevel: filters.exposureLevel,
            tag: filters.tag,
            unassignedOnly: filters.unassignedOnly,
          })
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
            criticality: '',
            ownerType: '',
            deviceGroup: '',
            healthStatus: '',
            onboardingStatus: '',
            riskScore: '',
            exposureLevel: '',
            tag: '',
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
