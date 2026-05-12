import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import {
  approveOrRejectDecision,
  fetchDecisionContext,
} from '@/api/remediation.functions'
import { ManagerApprovalCaseWorkbench } from '@/components/features/approvals/ManagerApprovalCaseWorkbench'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { getApiErrorMessage } from '@/lib/api-errors'

export const Route = createFileRoute('/_authed/workbenches/technical-manager/cases/$caseId')({
  loader: ({ params }) => fetchDecisionContext({ data: { caseId: params.caseId } }),
  component: TechnicalManagerApprovalCaseRoute,
})

function TechnicalManagerApprovalCaseRoute() {
  const initialData = Route.useLoaderData()
  const { caseId } = Route.useParams()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const queryClient = useQueryClient()
  const queryKey = ['technical-manager-approval-case', selectedTenantId, caseId] as const

  const query = useQuery({
    queryKey,
    queryFn: () => fetchDecisionContext({ data: { caseId } }),
    initialData: canUseInitialData ? initialData : undefined,
  })

  const data = query.data ?? (canUseInitialData ? initialData : undefined)
  if (!data) {
    return (
      <div className="flex items-center justify-center py-24 text-muted-foreground">
        Loading technical manager workbench...
      </div>
    )
  }

  return (
    <ManagerApprovalCaseWorkbench
      data={data}
      caseId={caseId}
      role="technical-manager"
      isSubmitting={isSubmitting}
      error={error}
      onResolve={async (action, justification, maintenanceWindowDate) => {
        if (!data.currentDecision) return
        setIsSubmitting(true)
        setError(null)
        try {
          await approveOrRejectDecision({
            data: {
              caseId,
              decisionId: data.currentDecision.id,
              action,
              justification,
              maintenanceWindowDate,
            },
          })
          await queryClient.invalidateQueries({ queryKey })
          await queryClient.invalidateQueries({ queryKey: ['technical-manager-approval-workbench'] })
        } catch (caught) {
          setError(getApiErrorMessage(caught, 'Unable to resolve the approval.'))
        } finally {
          setIsSubmitting(false)
        }
      }}
    />
  )
}
