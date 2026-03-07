import { useState } from 'react'
import type { VulnerabilityDetail } from '@/api/useVulnerabilities'
import { useUpdateOrganizationalSeverity } from '@/api/useVulnerabilities'

type OrgSeverityPanelProps = {
  vulnerability: VulnerabilityDetail
}

const severityOptions = ['Low', 'Medium', 'High', 'Critical']

export function OrgSeverityPanel({ vulnerability }: OrgSeverityPanelProps) {
  const mutation = useUpdateOrganizationalSeverity(vulnerability.id)
  const [adjustedSeverity, setAdjustedSeverity] = useState(
    vulnerability.organizationalSeverity?.adjustedSeverity ?? vulnerability.vendorSeverity,
  )
  const [justification, setJustification] = useState(vulnerability.organizationalSeverity?.justification ?? '')

  return (
    <section className="space-y-3 rounded-lg border border-border bg-card p-4">
      <h3 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">Organizational Severity</h3>
      <p className="text-sm">Current: {vulnerability.organizationalSeverity?.adjustedSeverity ?? 'Not set'}</p>
      <label className="block space-y-1 text-sm">
        <span>Adjusted severity</span>
        <select
          className="w-full rounded-md border border-input bg-background px-2 py-1.5"
          value={adjustedSeverity}
          onChange={(event) => {
            setAdjustedSeverity(event.target.value)
          }}
        >
          {severityOptions.map((value) => (
            <option key={value} value={value}>
              {value}
            </option>
          ))}
        </select>
      </label>
      <label className="block space-y-1 text-sm">
        <span>Justification</span>
        <textarea
          className="min-h-24 w-full rounded-md border border-input bg-background px-2 py-1.5"
          value={justification}
          onChange={(event) => {
            setJustification(event.target.value)
          }}
        />
      </label>
      <button
        type="button"
        className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90 disabled:opacity-50"
        disabled={mutation.isPending || justification.trim().length === 0}
        onClick={() => {
          mutation.mutate({
            adjustedSeverity,
            justification,
          })
        }}
      >
        {mutation.isPending ? 'Saving...' : 'Save adjustment'}
      </button>
      {mutation.isError ? <p className="text-sm text-destructive">Unable to save severity adjustment.</p> : null}
      {mutation.isSuccess ? <p className="text-sm text-emerald-600">Severity adjustment saved.</p> : null}
    </section>
  )
}
