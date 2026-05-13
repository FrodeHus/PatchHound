import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { fetchDecisionContext } from '@/api/remediation.functions'
import { SecurityAnalystWorkbench } from '@/components/features/remediation/SecurityAnalystWorkbench'
import { useTenantScope } from '@/components/layout/tenant-scope'

export const Route = createFileRoute('/_authed/workbenches/security-analyst/cases/$caseId')({
  loader: ({ params }) => fetchDecisionContext({ data: { caseId: params.caseId } }),
  component: SecurityAnalystWorkbenchRoute,
})

function SecurityAnalystWorkbenchRoute() {
  const initialData = Route.useLoaderData()
  const { caseId } = Route.useParams()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId
  const queryKey = ['security-analyst-workbench', selectedTenantId, caseId] as const

  const query = useQuery({
    queryKey,
    queryFn: () => fetchDecisionContext({ data: { caseId } }),
    initialData: canUseInitialData ? initialData : undefined,
    refetchInterval: (currentQuery) => {
      const status = currentQuery.state.data?.patchAssessment.jobStatus
      return status === 'Pending' || status === 'Running' ? 8000 : false
    },
    refetchIntervalInBackground: false,
  })

  const data = query.data ?? (canUseInitialData ? initialData : undefined)

  if (!data) {
    return (
      <div className="flex items-center justify-center py-24 text-muted-foreground">
        Loading analyst workbench...
      </div>
    )
  }

  return <SecurityAnalystWorkbench data={data} caseId={caseId} queryKey={queryKey} />
}
