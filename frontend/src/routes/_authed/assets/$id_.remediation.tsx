import { createFileRoute, redirect } from '@tanstack/react-router'
import { fetchAssetDetail } from '@/api/assets.functions'

export const Route = createFileRoute('/_authed/assets/$id_/remediation')({
  loader: async ({ params }) => {
    const asset = await fetchAssetDetail({ data: { assetId: params.id } })

    if (asset.assetType !== 'Software' || !asset.tenantSoftwareId) {
      throw redirect({ to: '/assets/$id', params: { id: params.id } })
    }

    throw redirect({ to: '/software/$id/remediation', params: { id: asset.tenantSoftwareId } })
  },
})
