import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchDecisionContext } from '@/api/remediation.functions'
import { SoftwareRemediationView } from '@/components/features/remediation/SoftwareRemediationView'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { assetQueryKeys } from '@/features/assets/list-state'

export const Route = createFileRoute('/_authed/assets/$id_/remediation')({
  loader: ({ params }) => fetchDecisionContext({ data: { assetId: params.id } }),
  component: RemediationPage,
})

function RemediationPage() {
  const initialData = Route.useLoaderData()
  const { id } = Route.useParams()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId

  const query = useQuery({
    queryKey: assetQueryKeys.remediation(selectedTenantId, id),
    queryFn: () => fetchDecisionContext({ data: { assetId: id } }),
    initialData: canUseInitialData ? initialData : undefined,
  })

  const data = query.data ?? (canUseInitialData ? initialData : undefined)

  if (!data) {
    return (
      <div className="flex items-center justify-center py-24 text-muted-foreground">
        Loading decision context...
      </div>
    )
  }

  return <SoftwareRemediationView data={data} assetId={id} />
}
