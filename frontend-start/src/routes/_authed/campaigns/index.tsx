import { createFileRoute } from '@tanstack/react-router'
import { fetchCampaigns } from '@/api/campaigns.functions'
import { CampaignList } from '@/components/features/campaigns/CampaignList'

export const Route = createFileRoute('/_authed/campaigns/')({
  loader: () => fetchCampaigns({ data: {} }),
  component: CampaignsPage,
})

function CampaignsPage() {
  const data = Route.useLoaderData()

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Campaigns</h1>
      <CampaignList data={data} />
    </section>
  )
}
