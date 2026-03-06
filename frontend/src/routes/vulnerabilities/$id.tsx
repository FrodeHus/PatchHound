import { createFileRoute } from '@tanstack/react-router'
import { useVulnerabilityDetail } from '@/api/useVulnerabilities'
import { VulnerabilityDetail } from '@/components/features/vulnerabilities/VulnerabilityDetail'

export const Route = createFileRoute('/vulnerabilities/$id')({
  component: VulnerabilityDetailPage,
})

function VulnerabilityDetailPage() {
  const { id } = Route.useParams()
  const detailQuery = useVulnerabilityDetail(id)

  if (detailQuery.isLoading) {
    return <p className="text-sm text-muted-foreground">Loading vulnerability details...</p>
  }

  if (detailQuery.isError || !detailQuery.data) {
    return <p className="text-sm text-destructive">Failed to load vulnerability details.</p>
  }

  return <VulnerabilityDetail vulnerability={detailQuery.data} />
}
