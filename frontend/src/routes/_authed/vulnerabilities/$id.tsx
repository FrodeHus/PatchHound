import { createFileRoute } from '@tanstack/react-router'
import { fetchVulnerabilityDetail } from '@/api/vulnerabilities.functions'
import { VulnerabilityDetail } from '@/components/features/vulnerabilities/VulnerabilityDetail'

export const Route = createFileRoute('/_authed/vulnerabilities/$id')({
  loader: ({ params }) => fetchVulnerabilityDetail({ data: { id: params.id } }),
  component: VulnerabilityDetailPage,
})

function VulnerabilityDetailPage() {
  const detail = Route.useLoaderData()

  return (
    <section className="space-y-4">
      <VulnerabilityDetail vulnerability={detail} />
    </section>
  )
}
