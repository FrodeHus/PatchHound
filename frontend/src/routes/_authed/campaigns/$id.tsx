import { createFileRoute } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { bulkAssignCampaign, fetchCampaignDetail, linkCampaignVulnerabilities } from '@/api/campaigns.functions'
import { CampaignDetail } from '@/components/features/campaigns/CampaignDetail'

export const Route = createFileRoute('/_authed/campaigns/$id')({
  loader: ({ params }) => fetchCampaignDetail({ data: { id: params.id } }),
  component: CampaignDetailPage,
})

function CampaignDetailPage() {
  const detail = Route.useLoaderData()
  const linkMutation = useMutation({
    mutationFn: async (vulnerabilityIds: string[]) => {
      await linkCampaignVulnerabilities({
        data: {
          campaignId: detail.id,
          vulnerabilityIds,
        },
      })
    },
  })
  const assignMutation = useMutation({
    mutationFn: async (assigneeId: string) => {
      await bulkAssignCampaign({
        data: {
          campaignId: detail.id,
          assigneeId,
        },
      })
    },
  })

  return (
    <section className="space-y-4">
      <CampaignDetail
        campaign={detail}
        isLinking={linkMutation.isPending}
        isAssigning={assignMutation.isPending}
        onLinkVulnerabilities={(ids) => {
          linkMutation.mutate(ids)
        }}
        onBulkAssign={(assigneeId) => {
          assignMutation.mutate(assigneeId)
        }}
      />
    </section>
  )
}
