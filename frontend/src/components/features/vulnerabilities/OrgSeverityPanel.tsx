import { useMutation } from '@tanstack/react-query'
import { toast } from 'sonner'
import { updateOrganizationalSeverity } from '@/api/vulnerabilities.functions'
import type { VulnerabilityDetail } from '@/api/vulnerabilities.schemas'
import { getApiErrorMessage } from '@/lib/api-errors'

type OrgSeverityPanelProps = {
  vulnerability: VulnerabilityDetail
}

export function OrgSeverityPanel({ vulnerability }: OrgSeverityPanelProps) {
  // The organizational severity endpoint returns 409 in Phase 2 (pending Phase 3 exposure model).
  // This component is intentionally a no-op stub until Phase 3 wires up the new data model.
  const _mutation = useMutation({
    mutationFn: (payload: { adjustedSeverity: string; justification: string }) =>
      updateOrganizationalSeverity({
        data: {
          id: vulnerability.id,
          ...payload,
        },
      }),
    onSuccess: () => {
      toast.success('Severity adjustment saved')
    },
    onError: (error) => {
      toast.error(getApiErrorMessage(error, 'Failed to save severity adjustment'))
    },
  })

  return (
    <section className="space-y-4 rounded-xl border border-border/70 bg-background px-4 py-4">
      <div>
        <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Organizational Severity</p>
        <h4 className="mt-1 text-sm font-semibold text-foreground">Not available in Phase 2</h4>
        <p className="mt-1 text-xs text-muted-foreground">
          Organizational severity adjustments will be re-introduced in Phase 3 alongside the new exposure data model.
        </p>
      </div>
    </section>
  )
}
