import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/campaigns/')({
  component: CampaignsPage,
})

function CampaignsPage() {
  return (
    <section className="space-y-2">
      <h1 className="text-2xl font-semibold">Campaigns</h1>
      <p className="text-sm text-muted-foreground">Campaign list and progress overview will be implemented later.</p>
    </section>
  )
}
