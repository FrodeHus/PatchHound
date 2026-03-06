import { createFileRoute } from '@tanstack/react-router'
import { CampaignDetail } from '@/components/features/campaigns/CampaignDetail'
import {
  useBulkAssignCampaign,
  useCampaignDetail,
  useLinkCampaignVulnerabilities,
} from '@/api/useCampaigns'

export const Route = createFileRoute('/campaigns/$id')({
  component: CampaignDetailPage,
})

function CampaignDetailPage() {
  const { id } = Route.useParams()
  const detailQuery = useCampaignDetail(id)
  const linkMutation = useLinkCampaignVulnerabilities(id)
  const assignMutation = useBulkAssignCampaign(id)

  if (detailQuery.isLoading) {
    return <p className="text-sm text-muted-foreground">Loading campaign details...</p>
  }

  if (detailQuery.isError || !detailQuery.data) {
    return <p className="text-sm text-destructive">Failed to load campaign details.</p>
  }

  return (
    <CampaignDetail
      campaign={detailQuery.data}
      isLinking={linkMutation.isPending}
      isAssigning={assignMutation.isPending}
      onLinkVulnerabilities={(ids) => {
        linkMutation.mutate(ids)
      }}
      onBulkAssign={(assigneeId) => {
        assignMutation.mutate(assigneeId)
      }}
    />
  )
}
