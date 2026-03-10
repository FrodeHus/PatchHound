import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { assignAssetSecurityProfile, fetchAssetDetail } from '@/api/assets.functions'
import { fetchSecurityProfiles } from '@/api/security-profiles.functions'
import { AssetDetailPageView } from '@/components/features/assets/AssetDetailPageView'
import { assetQueryKeys } from '@/features/assets/list-state'

export const Route = createFileRoute('/_authed/assets/$id')({
  loader: ({ params }) => fetchAssetDetail({ data: { assetId: params.id } }),
  component: AssetDetailPage,
})

function AssetDetailPage() {
  const initialAsset = Route.useLoaderData()
  const { id } = Route.useParams()
  const queryClient = useQueryClient()

  const assetQuery = useQuery({
    queryKey: assetQueryKeys.detail(id),
    queryFn: () => fetchAssetDetail({ data: { assetId: id } }),
    initialData: initialAsset,
  })
  const securityProfilesQuery = useQuery({
    queryKey: ['security-profiles'],
    queryFn: () => fetchSecurityProfiles({ data: {} }),
  })
  const securityProfileMutation = useMutation({
    mutationFn: async (securityProfileId: string | null) => {
      await assignAssetSecurityProfile({ data: { assetId: id, securityProfileId } })
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: assetQueryKeys.detail(id) })
      await queryClient.invalidateQueries({ queryKey: assetQueryKeys.all })
    },
  })
  return (
    <AssetDetailPageView
      asset={assetQuery.data}
      securityProfiles={securityProfilesQuery.data?.items ?? []}
      isAssigningSecurityProfile={securityProfileMutation.isPending}
      onAssignSecurityProfile={(_, securityProfileId) => {
        securityProfileMutation.mutate(securityProfileId)
      }}
    />
  )
}
