import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { toast } from 'sonner'
import { assignAssetSecurityProfile, fetchAssetDetail, resetAssetCriticalityOverride, setAssetCriticality } from '@/api/assets.functions'
import { fetchSecurityProfiles } from '@/api/security-profiles.functions'
import { AssetDetailPageView } from '@/components/features/assets/AssetDetailPageView'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { assetQueryKeys } from '@/features/assets/list-state'

export const Route = createFileRoute('/_authed/assets/$id')({
  loader: ({ params }) => fetchAssetDetail({ data: { assetId: params.id } }),
  component: AssetDetailPage,
})

function AssetDetailPage() {
  const initialAsset = Route.useLoaderData()
  const { id } = Route.useParams()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId
  const queryClient = useQueryClient()

  const assetQuery = useQuery({
    queryKey: assetQueryKeys.detail(selectedTenantId, id),
    queryFn: () => fetchAssetDetail({ data: { assetId: id } }),
    initialData: canUseInitialData ? initialAsset : undefined,
  })
  const asset = assetQuery.data ?? (canUseInitialData ? initialAsset : undefined)
  const securityProfilesQuery = useQuery({
    queryKey: ['security-profiles', selectedTenantId],
    queryFn: () => fetchSecurityProfiles({ data: {} }),
  })
  const securityProfileMutation = useMutation({
    mutationFn: async (securityProfileId: string | null) => {
      await assignAssetSecurityProfile({ data: { assetId: id, securityProfileId } })
    },
    onSuccess: async () => {
      toast.success('Security profile assigned')
      await queryClient.invalidateQueries({ queryKey: assetQueryKeys.detail(selectedTenantId, id) })
      await queryClient.invalidateQueries({ queryKey: assetQueryKeys.all })
    },
    onError: () => {
      toast.error('Failed to assign security profile')
    },
  })
  const setCriticalityMutation = useMutation({
    mutationFn: async (criticality: string) => {
      await setAssetCriticality({ data: { assetId: id, criticality } })
    },
    onSuccess: async () => {
      toast.success('Criticality updated')
      await queryClient.invalidateQueries({ queryKey: assetQueryKeys.detail(selectedTenantId, id) })
      await queryClient.invalidateQueries({ queryKey: assetQueryKeys.all })
    },
    onError: () => {
      toast.error('Failed to update criticality')
    },
  })
  const resetCriticalityMutation = useMutation({
    mutationFn: async () => {
      await resetAssetCriticalityOverride({ data: { assetId: id } })
    },
    onSuccess: async () => {
      toast.success('Manual criticality override removed')
      await queryClient.invalidateQueries({ queryKey: assetQueryKeys.detail(selectedTenantId, id) })
      await queryClient.invalidateQueries({ queryKey: assetQueryKeys.all })
    },
    onError: () => {
      toast.error('Failed to remove manual criticality override')
    },
  })
  if (!asset) {
    return null
  }

  return (
    <AssetDetailPageView
      asset={asset}
      securityProfiles={securityProfilesQuery.data?.items ?? []}
      isAssigningSecurityProfile={securityProfileMutation.isPending}
      isSettingCriticality={setCriticalityMutation.isPending}
      isResettingCriticality={resetCriticalityMutation.isPending}
      onAssignSecurityProfile={(_, securityProfileId) => {
        securityProfileMutation.mutate(securityProfileId)
      }}
      onSetCriticality={(criticality) => {
        setCriticalityMutation.mutate(criticality)
      }}
      onResetCriticality={() => {
        resetCriticalityMutation.mutate()
      }}
    />
  )
}
