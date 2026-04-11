import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { toast } from 'sonner'
import {
  assignDeviceBusinessLabels,
  assignDeviceSecurityProfile,
  fetchDeviceDetail,
  resetDeviceCriticalityOverride,
  setDeviceCriticality,
} from '@/api/devices.functions'
import { fetchBusinessLabels } from '@/api/business-labels.functions'
import { fetchSecurityProfiles } from '@/api/security-profiles.functions'
import { DeviceDetailPageView } from '@/components/features/devices/DeviceDetailPageView'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { deviceQueryKeys } from '@/features/devices/list-state'

export const Route = createFileRoute('/_authed/devices/$id')({
  loader: ({ params }) => fetchDeviceDetail({ data: { deviceId: params.id } }),
  component: DeviceDetailPage,
})

function DeviceDetailPage() {
  const initialDevice = Route.useLoaderData()
  const { user } = Route.useRouteContext()
  const { id } = Route.useParams()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId
  const queryClient = useQueryClient()

  const deviceQuery = useQuery({
    queryKey: deviceQueryKeys.detail(selectedTenantId, id),
    queryFn: () => fetchDeviceDetail({ data: { deviceId: id } }),
    initialData: canUseInitialData ? initialDevice : undefined,
  })
  const device = deviceQuery.data ?? (canUseInitialData ? initialDevice : undefined)
  const securityProfilesQuery = useQuery({
    queryKey: ['security-profiles', selectedTenantId],
    queryFn: () => fetchSecurityProfiles({ data: {} }),
  })
  const businessLabelsQuery = useQuery({
    queryKey: ['business-labels', selectedTenantId],
    queryFn: () => fetchBusinessLabels({ data: {} }),
  })
  const securityProfileMutation = useMutation({
    mutationFn: async (securityProfileId: string | null) => {
      await assignDeviceSecurityProfile({ data: { deviceId: id, securityProfileId } })
    },
    onSuccess: async () => {
      toast.success('Security profile assigned')
      await queryClient.invalidateQueries({ queryKey: deviceQueryKeys.detail(selectedTenantId, id) })
      await queryClient.invalidateQueries({ queryKey: deviceQueryKeys.all })
    },
    onError: () => {
      toast.error('Failed to assign security profile')
    },
  })
  const setCriticalityMutation = useMutation({
    mutationFn: async (criticality: string) => {
      await setDeviceCriticality({ data: { deviceId: id, criticality } })
    },
    onSuccess: async () => {
      toast.success('Criticality updated')
      await queryClient.invalidateQueries({ queryKey: deviceQueryKeys.detail(selectedTenantId, id) })
      await queryClient.invalidateQueries({ queryKey: deviceQueryKeys.all })
    },
    onError: () => {
      toast.error('Failed to update criticality')
    },
  })
  const resetCriticalityMutation = useMutation({
    mutationFn: async () => {
      await resetDeviceCriticalityOverride({ data: { deviceId: id } })
    },
    onSuccess: async () => {
      toast.success('Manual criticality override removed')
      await queryClient.invalidateQueries({ queryKey: deviceQueryKeys.detail(selectedTenantId, id) })
      await queryClient.invalidateQueries({ queryKey: deviceQueryKeys.all })
    },
    onError: () => {
      toast.error('Failed to remove manual criticality override')
    },
  })
  const businessLabelsMutation = useMutation({
    mutationFn: async (businessLabelIds: string[]) => {
      await assignDeviceBusinessLabels({ data: { deviceId: id, businessLabelIds } })
    },
    onSuccess: async () => {
      toast.success('Business labels updated')
      await queryClient.invalidateQueries({ queryKey: deviceQueryKeys.detail(selectedTenantId, id) })
      await queryClient.invalidateQueries({ queryKey: deviceQueryKeys.all })
      await queryClient.invalidateQueries({ queryKey: ['business-labels', selectedTenantId] })
    },
    onError: () => {
      toast.error('Failed to update business labels')
    },
  })
  if (!device) {
    return null
  }

  return (
    <DeviceDetailPageView
      device={device}
      canUseAdvancedTools={
        (user.activeRoles ?? []).includes('GlobalAdmin')
        || (user.activeRoles ?? []).includes('SecurityManager')
      }
      securityProfiles={securityProfilesQuery.data?.items ?? []}
      availableBusinessLabels={businessLabelsQuery.data ?? []}
      isAssigningSecurityProfile={securityProfileMutation.isPending}
      isSettingCriticality={setCriticalityMutation.isPending}
      isResettingCriticality={resetCriticalityMutation.isPending}
      isAssigningBusinessLabels={businessLabelsMutation.isPending}
      onAssignSecurityProfile={(_, securityProfileId) => {
        securityProfileMutation.mutate(securityProfileId)
      }}
      onSetCriticality={(criticality) => {
        setCriticalityMutation.mutate(criticality)
      }}
      onResetCriticality={() => {
        resetCriticalityMutation.mutate()
      }}
      onAssignBusinessLabels={(businessLabelIds) => {
        businessLabelsMutation.mutate(businessLabelIds)
      }}
    />
  )
}
