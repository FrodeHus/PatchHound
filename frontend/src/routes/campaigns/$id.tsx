import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/campaigns/$id')({
  component: CampaignDetailPage,
})

function CampaignDetailPage() {
  const { id } = Route.useParams()

  return (
    <section className="space-y-2">
      <h1 className="text-2xl font-semibold">Campaign {id}</h1>
      <p className="text-sm text-muted-foreground">Campaign details and bulk actions will be implemented later.</p>
    </section>
  )
}
