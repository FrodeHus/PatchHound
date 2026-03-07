import { createFileRoute } from '@tanstack/react-router'
import { fetchCampaignDetail } from '@/api/campaigns.functions'
import { CampaignDetail } from '@/components/features/campaigns/CampaignDetail'

export const Route = createFileRoute('/_authed/campaigns/$id')({
  loader: ({ params }) => fetchCampaignDetail({ data: { id: params.id } }),
  component: CampaignDetailPage,
})

function CampaignDetailPage() {
  const detail = Route.useLoaderData()

  return (
    <section className="space-y-4">
      <CampaignDetail data={detail} />
    </section>
  )
}
