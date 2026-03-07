import { useMutation, useQuery } from '@tanstack/react-query'
import { createFileRoute, useRouter } from '@tanstack/react-router'
import { assignAssetSecurityProfile, fetchAssetDetail } from '@/api/assets.functions'
import { fetchSecurityProfiles } from '@/api/security-profiles.functions'
import { AssetDetailPageView } from '@/components/features/assets/AssetDetailPageView'

export const Route = createFileRoute('/_authed/assets/$id')({
  loader: ({ params }) => fetchAssetDetail({ data: { assetId: params.id } }),
  component: AssetDetailPage,
})

function AssetDetailPage() {
  const initialAsset = Route.useLoaderData()
  const { id } = Route.useParams()
  const router = useRouter()

  const assetQuery = useQuery({
    queryKey: ['asset-page', id],
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
      await router.invalidate()
      await assetQuery.refetch()
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
