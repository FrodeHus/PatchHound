import { useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { fetchVulnerabilities } from '@/api/vulnerabilities.functions'
import { VulnerabilityTable } from '@/components/features/vulnerabilities/VulnerabilityTable'

export const Route = createFileRoute('/_authed/vulnerabilities/')({
  loader: () => fetchVulnerabilities({ data: {} }),
  component: VulnerabilitiesPage,
})

function VulnerabilitiesPage() {
  const initialData = Route.useLoaderData()
  const [recurrenceOnly, setRecurrenceOnly] = useState(false)
  const query = useQuery({
    queryKey: ['vulnerabilities', recurrenceOnly],
    queryFn: () => fetchVulnerabilities({ data: recurrenceOnly ? { recurrenceOnly: true } : {} }),
    initialData,
  })

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Vulnerabilities</h1>
      <VulnerabilityTable
        items={query.data.items}
        totalCount={query.data.totalCount}
        recurrenceOnly={recurrenceOnly}
        onRecurrenceOnlyChange={setRecurrenceOnly}
      />
    </section>
  )
}
