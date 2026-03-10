import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { updateOrganizationalSeverity } from '@/api/vulnerabilities.functions'
import type { VulnerabilityDetail } from '@/api/vulnerabilities.schemas'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { vulnerabilitySeverityOptions } from '@/lib/options/vulnerabilities'

type OrgSeverityPanelProps = {
  vulnerability: VulnerabilityDetail
}

export function OrgSeverityPanel({ vulnerability }: OrgSeverityPanelProps) {
  const mutation = useMutation({
    mutationFn: (payload: { adjustedSeverity: string; justification: string }) =>
      updateOrganizationalSeverity({
        data: {
          id: vulnerability.id,
          ...payload,
        },
      }),
  })
  const [adjustedSeverity, setAdjustedSeverity] = useState(
    vulnerability.organizationalSeverity?.adjustedSeverity ?? vulnerability.vendorSeverity,
  )
  const [justification, setJustification] = useState(vulnerability.organizationalSeverity?.justification ?? '')
  const hasExistingAdjustment = vulnerability.organizationalSeverity !== null

  return (
    <section className="space-y-4 rounded-xl border border-border/70 bg-background px-4 py-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
          <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Organizational Severity</p>
          <h4 className="text-sm font-semibold text-foreground">
            {hasExistingAdjustment ? 'Analyst adjustment in effect' : 'No analyst adjustment recorded'}
          </h4>
        </div>
        <span
          className={`rounded-full border px-3 py-1 text-[11px] font-medium uppercase tracking-[0.14em] ${
            hasExistingAdjustment
              ? 'border-amber-300/70 bg-amber-50 text-amber-900'
              : 'border-border/70 bg-card text-muted-foreground'
          }`}
        >
          {vulnerability.organizationalSeverity?.adjustedSeverity ?? 'Not adjusted'}
        </span>
      </div>

      <div className="grid gap-3 md:grid-cols-[180px_minmax(0,1fr)]">
        <label className="space-y-1.5 text-sm">
          <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Adjusted severity</span>
          <Select
            value={adjustedSeverity}
            onValueChange={(value) => {
              if (value) {
                setAdjustedSeverity(value)
              }
            }}
          >
            <SelectTrigger className="h-10 w-full rounded-lg bg-card px-3">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {vulnerabilitySeverityOptions.map((value) => (
                <SelectItem key={value} value={value}>
                  {value}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </label>

        <label className="space-y-1.5 text-sm">
          <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Justification</span>
          <Textarea
            className="min-h-28 rounded-lg bg-card leading-6"
            value={justification}
            placeholder="Explain why the effective severity should differ from the vendor severity in this organization."
            onChange={(event) => {
              setJustification(event.target.value)
            }}
          />
        </label>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border/70 pt-3">
        <p className="text-xs text-muted-foreground">
          This adjustment is used as the tenant-specific analyst view of the vulnerability.
        </p>
        <Button
          type="button"
          className="rounded-lg"
          disabled={mutation.isPending || justification.trim().length === 0}
          onClick={() => {
            mutation.mutate({
              adjustedSeverity,
              justification,
            })
          }}
        >
          {mutation.isPending ? 'Saving...' : 'Save adjustment'}
        </Button>
      </div>

      {mutation.isError ? <p className="text-sm text-destructive">Unable to save severity adjustment.</p> : null}
      {mutation.isSuccess ? <p className="text-sm text-emerald-600">Severity adjustment saved.</p> : null}
    </section>
  )
}
