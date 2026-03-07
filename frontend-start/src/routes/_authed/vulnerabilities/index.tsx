import { createFileRoute } from '@tanstack/react-router'
import { fetchVulnerabilities } from '@/api/vulnerabilities.functions'
import { VulnerabilityTable } from '@/components/features/vulnerabilities/VulnerabilityTable'

export const Route = createFileRoute('/_authed/vulnerabilities/')({
  loader: () => fetchVulnerabilities({ data: {} }),
  component: VulnerabilitiesPage,
})

function VulnerabilitiesPage() {
  const data = Route.useLoaderData()

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Vulnerabilities</h1>
      <VulnerabilityTable data={data} />
    </section>
  )
}
