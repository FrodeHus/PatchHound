import { useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import {
  assignDeviceOwner,
  assignDeviceSecurityProfile,
  fetchDeviceDetail,
  fetchDevices,
  setDeviceCriticality,
} from '@/api/devices.functions'
import { fetchBusinessLabels } from '@/api/business-labels.functions'
import { DeviceDetailPane } from '@/components/features/devices/DeviceDetailPane'
import { DeviceManagementTable } from '@/components/features/devices/DeviceManagementTable'
import { useTenantScope } from '@/components/layout/tenant-scope'
import {
  buildDevicesListRequest,
  deviceQueryKeys,
  type DevicesListSearch,
} from '@/features/devices/list-state'
import { baseListSearchSchema, searchBooleanSchema, searchStringSchema } from '@/routes/-list-search'
import { createListSearchUpdater } from '@/routes/-list-search-helpers'

const devicesSearchSchema = baseListSearchSchema.extend({
  search: searchStringSchema,
  criticality: searchStringSchema,
  businessLabelId: searchStringSchema,
  ownerType: searchStringSchema,
  deviceGroup: searchStringSchema,
  healthStatus: searchStringSchema,
  onboardingStatus: searchStringSchema,
  riskScore: searchStringSchema,
  exposureLevel: searchStringSchema,
  tag: searchStringSchema,
  unassignedOnly: searchBooleanSchema,
})

export const Route = createFileRoute('/_authed/devices/')({
  validateSearch: devicesSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: async ({ deps }) => {
    const [devices, businessLabels] = await Promise.all([
      fetchDevices({ data: buildDevicesListRequest(deps) }),
      fetchBusinessLabels({ data: {} }),
    ])
    return { devices, businessLabels }
  },
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
  const [selectedDeviceId, setSelectedDeviceId] = useState<string | null>(null)
  const deviceSearch: DevicesListSearch = search
  const devicesQuery = useQuery({
    queryKey: deviceQueryKeys.list(selectedTenantId, deviceSearch),
    queryFn: () => fetchDevices({ data: buildDevicesListRequest(deviceSearch) }),
    initialData: canUseInitialData ? initialData.devices : undefined,
  })
  const businessLabelsQuery = useQuery({
    queryKey: ['business-labels', selectedTenantId],
    queryFn: () => fetchBusinessLabels({ data: {} }),
    initialData: canUseInitialData ? initialData.businessLabels : undefined,
  })
  const devices = devicesQuery.data ?? (canUseInitialData ? initialData.devices : undefined)
  const businessLabels = businessLabelsQuery.data ?? (canUseInitialData ? initialData.businessLabels : [])
  const ownerMutation = useMutation({
    mutationFn: async (payload: { deviceId: string; ownerType: 'User' | 'Team'; ownerId: string }) => {
      await assignDeviceOwner({
        data: {
          deviceId: payload.deviceId,
          ownerType: payload.ownerType,
          ownerId: payload.ownerId,
        },
      })
    },
    onSuccess: async () => {
      toast.success('Owner assigned')
      await queryClient.invalidateQueries({ queryKey: deviceQueryKeys.all })
    },
    onError: () => {
      toast.error('Failed to assign owner')
    },
  })
  const criticalityMutation = useMutation({
    mutationFn: async (payload: { deviceId: string; criticality: string }) => {
      await setDeviceCriticality({
        data: {
          deviceId: payload.deviceId,
          criticality: payload.criticality,
        },
      })
    },
    onSuccess: async () => {
      toast.success('Criticality updated')
      await queryClient.invalidateQueries({ queryKey: deviceQueryKeys.all })
    },
    onError: () => {
      toast.error('Failed to update criticality')
    },
  })
  const securityProfileMutation = useMutation({
    mutationFn: async (payload: { deviceId: string; securityProfileId: string | null }) => {
      await assignDeviceSecurityProfile({ data: payload })
    },
    onSuccess: async () => {
      toast.success('Security profile assigned')
      if (selectedDeviceId) {
        await queryClient.invalidateQueries({ queryKey: deviceQueryKeys.detail(selectedTenantId, selectedDeviceId) })
      }
      await queryClient.invalidateQueries({ queryKey: deviceQueryKeys.all })
    },
    onError: () => {
      toast.error('Failed to assign security profile')
    },
  })
  const deviceDetailQuery = useQuery({
    queryKey: deviceQueryKeys.detail(selectedTenantId, selectedDeviceId),
    queryFn: () => fetchDeviceDetail({ data: { deviceId: selectedDeviceId! } }),
    enabled: Boolean(selectedDeviceId),
  })

  if (!devices) {
    return null
  }

  return (
    <section className="space-y-4">
      <DeviceManagementTable
        title="Devices"
        description="Work the endpoint fleet with device-centric filters, then open the inspector from the device name."
        searchPlaceholder="Search devices"
        devices={devices.items}
        totalCount={devices.totalCount}
        isUpdating={ownerMutation.isPending || criticalityMutation.isPending || securityProfileMutation.isPending}
        selectedDeviceId={selectedDeviceId}
        searchValue={deviceSearch.search}
        criticalityFilter={deviceSearch.criticality}
        businessLabelIdFilter={deviceSearch.businessLabelId}
        availableBusinessLabels={businessLabels}
        ownerTypeFilter={deviceSearch.ownerType}
        deviceGroupFilter={deviceSearch.deviceGroup}
        healthStatusFilter={deviceSearch.healthStatus}
        onboardingStatusFilter={deviceSearch.onboardingStatus}
        riskScoreFilter={deviceSearch.riskScore}
        exposureLevelFilter={deviceSearch.exposureLevel}
        tagFilter={deviceSearch.tag}
        unassignedOnly={deviceSearch.unassignedOnly}
        page={devices.page}
        pageSize={devices.pageSize}
        totalPages={devices.totalPages}
        onSearchChange={(searchValue) => {
          searchActions.updateField('search', searchValue)
          setSelectedDeviceId(null)
        }}
        onCriticalityFilterChange={(criticality) => {
          searchActions.updateField('criticality', criticality)
          setSelectedDeviceId(null)
        }}
        onOwnerTypeFilterChange={(ownerType) => {
          searchActions.updateField('ownerType', ownerType)
          setSelectedDeviceId(null)
        }}
        onBusinessLabelFilterChange={(businessLabelId) => {
          searchActions.updateField('businessLabelId', businessLabelId)
          setSelectedDeviceId(null)
        }}
        onDeviceGroupFilterChange={(deviceGroup) => {
          searchActions.updateField('deviceGroup', deviceGroup)
          setSelectedDeviceId(null)
        }}
        onHealthStatusFilterChange={(healthStatus) => {
          searchActions.updateField('healthStatus', healthStatus)
          setSelectedDeviceId(null)
        }}
        onOnboardingStatusFilterChange={(onboardingStatus) => {
          searchActions.updateField('onboardingStatus', onboardingStatus)
          setSelectedDeviceId(null)
        }}
        onRiskScoreFilterChange={(riskScore) => {
          searchActions.updateField('riskScore', riskScore)
          setSelectedDeviceId(null)
        }}
        onExposureLevelFilterChange={(exposureLevel) => {
          searchActions.updateField('exposureLevel', exposureLevel)
          setSelectedDeviceId(null)
        }}
        onTagFilterChange={(tag) => {
          searchActions.updateField('tag', tag)
          setSelectedDeviceId(null)
        }}
        onUnassignedOnlyChange={(value) => {
          searchActions.updateField('unassignedOnly', value)
          setSelectedDeviceId(null)
        }}
        onApplyStructuredFilters={(filters) => {
          searchActions.updateFields({
            criticality: filters.criticality,
            businessLabelId: filters.businessLabelId,
            ownerType: filters.ownerType,
            deviceGroup: filters.deviceGroup,
            healthStatus: filters.healthStatus,
            onboardingStatus: filters.onboardingStatus,
            riskScore: filters.riskScore,
            exposureLevel: filters.exposureLevel,
            tag: filters.tag,
            unassignedOnly: filters.unassignedOnly,
          })
          setSelectedDeviceId(null)
        }}
        onPageChange={(page) => {
          searchActions.updatePage(page)
        }}
        onPageSizeChange={(nextPageSize) => {
          searchActions.updatePageSize(nextPageSize)
          setSelectedDeviceId(null)
        }}
        onClearFilters={() => {
          searchActions.updateFields({
            search: '',
            criticality: '',
            businessLabelId: '',
            ownerType: '',
            deviceGroup: '',
            healthStatus: '',
            onboardingStatus: '',
            riskScore: '',
            exposureLevel: '',
            tag: '',
            unassignedOnly: false,
          })
          setSelectedDeviceId(null)
        }}
        onSelectDevice={setSelectedDeviceId}
        onAssignOwner={(deviceId, ownerType, ownerId) => {
          ownerMutation.mutate({ deviceId, ownerType, ownerId })
        }}
        onSetCriticality={(deviceId, criticality) => {
          criticalityMutation.mutate({ deviceId, criticality })
        }}
      />
      <DeviceDetailPane
        device={deviceDetailQuery.data ?? null}
        isLoading={deviceDetailQuery.isLoading}
        isOpen={selectedDeviceId !== null}
        onOpenChange={(open) => {
          if (!open) {
            setSelectedDeviceId(null)
          }
        }}
      />
    </section>
  )
}
