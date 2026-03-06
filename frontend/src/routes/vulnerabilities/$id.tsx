import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/vulnerabilities/$id')({
  component: VulnerabilityDetailPage,
})

function VulnerabilityDetailPage() {
  const { id } = Route.useParams()

  return (
    <section className="space-y-2">
      <h1 className="text-2xl font-semibold">Vulnerability {id}</h1>
      <p className="text-sm text-muted-foreground">Detail tabs and actions will be added in the next task.</p>
    </section>
  )
}
