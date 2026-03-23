import type { SoftwareRemediationVuln } from '@/api/remediation.schemas'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { toneBadge, type Tone } from '@/lib/tone-classes'
import { formatDateTime } from '@/lib/formatting'

type RemediationVulnDrawerProps = {
  vuln: SoftwareRemediationVuln | null
  isOpen: boolean
  onOpenChange: (open: boolean) => void
}

function severityTone(severity: string): Tone {
  switch (severity) {
    case 'Critical': return 'danger'
    case 'High': return 'warning'
    case 'Medium': return 'info'
    default: return 'neutral'
  }
}

function InfoRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-4 py-1.5">
      <span className="shrink-0 text-xs text-muted-foreground">{label}</span>
      <span className="text-right text-sm">{children}</span>
    </div>
  )
}

export function RemediationVulnDrawer({ vuln, isOpen, onOpenChange }: RemediationVulnDrawerProps) {
  return (
    <Sheet open={isOpen} onOpenChange={onOpenChange}>
      <SheetContent
        side="right"
        className="w-full overflow-y-auto border-l border-border/80 bg-card p-0 sm:max-w-md"
      >
        <SheetHeader className="border-b border-border/70 p-4">
          <SheetTitle className="text-base">{vuln?.externalId ?? 'Vulnerability'}</SheetTitle>
          <SheetDescription className="line-clamp-2">{vuln?.title ?? ''}</SheetDescription>
        </SheetHeader>

        {vuln ? (
          <div className="space-y-5 p-4">
            {/* Severity & Score */}
            <section className="space-y-1">
              <h4 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Severity</h4>
              <div className="divide-y divide-border/50 rounded-lg border border-border/70 bg-background">
                <InfoRow label="Effective">
                  <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-medium ${toneBadge(severityTone(vuln.effectiveSeverity))}`}>
                    {vuln.effectiveSeverity}
                    {vuln.effectiveScore != null ? ` ${vuln.effectiveScore.toFixed(1)}` : ''}
                  </span>
                </InfoRow>
                <InfoRow label="Vendor">
                  <span className="text-xs">
                    {vuln.vendorSeverity}
                    {vuln.vendorScore != null ? ` (${vuln.vendorScore.toFixed(1)})` : ''}
                  </span>
                </InfoRow>
                {vuln.cvssVector ? (
                  <InfoRow label="CVSS Vector">
                    <code className="break-all text-[11px] text-muted-foreground">{vuln.cvssVector}</code>
                  </InfoRow>
                ) : null}
              </div>
            </section>

            {/* Threat Intelligence */}
            {vuln.threat ? (
              <section className="space-y-1">
                <h4 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Threat Intelligence</h4>
                <div className="divide-y divide-border/50 rounded-lg border border-border/70 bg-background">
                  <InfoRow label="EPSS Score">
                    {vuln.threat.epssScore != null
                      ? <span className="tabular-nums">{(vuln.threat.epssScore * 100).toFixed(2)}%</span>
                      : <span className="text-muted-foreground">-</span>}
                  </InfoRow>
                  <InfoRow label="Known Exploited (KEV)">
                    {vuln.threat.knownExploited
                      ? <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-medium ${toneBadge('danger')}`}>Yes</span>
                      : 'No'}
                  </InfoRow>
                  <InfoRow label="Public Exploit">
                    {vuln.threat.publicExploit
                      ? <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-medium ${toneBadge('warning')}`}>Yes</span>
                      : 'No'}
                  </InfoRow>
                  <InfoRow label="Active Alert">
                    {vuln.threat.activeAlert
                      ? <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-medium ${toneBadge('warning')}`}>Yes</span>
                      : 'No'}
                  </InfoRow>
                  <InfoRow label="Ransomware">
                    {vuln.threat.hasRansomwareAssociation
                      ? <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-medium ${toneBadge('danger')}`}>Associated</span>
                      : 'No'}
                  </InfoRow>
                </div>
              </section>
            ) : null}

            {/* Match Details */}
            <section className="space-y-1">
              <h4 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Match Details</h4>
              <div className="divide-y divide-border/50 rounded-lg border border-border/70 bg-background">
                <InfoRow label="Method">{vuln.matchMethod}</InfoRow>
                <InfoRow label="Confidence">{vuln.confidence}</InfoRow>
                <InfoRow label="First Seen">{formatDateTime(vuln.firstSeenAt)}</InfoRow>
                {vuln.resolvedAt ? (
                  <InfoRow label="Resolved">{formatDateTime(vuln.resolvedAt)}</InfoRow>
                ) : null}
              </div>
            </section>

            {/* Remediation Task */}
            {vuln.remediationTask ? (
              <section className="space-y-1">
                <h4 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Remediation Task</h4>
                <div className="divide-y divide-border/50 rounded-lg border border-border/70 bg-background">
                  <InfoRow label="Status">
                    <span className="text-xs font-medium">{vuln.remediationTask.status}</span>
                  </InfoRow>
                  <InfoRow label="Due Date">{formatDateTime(vuln.remediationTask.dueDate)}</InfoRow>
                  <InfoRow label="Created">{formatDateTime(vuln.remediationTask.createdAt)}</InfoRow>
                  {vuln.remediationTask.justification ? (
                    <div className="px-3 py-2">
                      <span className="text-xs text-muted-foreground">Justification</span>
                      <p className="mt-1 text-sm">{vuln.remediationTask.justification}</p>
                    </div>
                  ) : null}
                </div>
              </section>
            ) : null}

            {/* Risk Acceptance */}
            {vuln.riskAcceptance ? (
              <section className="space-y-1">
                <h4 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Risk Acceptance</h4>
                <div className="divide-y divide-border/50 rounded-lg border border-border/70 bg-background">
                  <InfoRow label="Status">
                    <span className="text-xs font-medium">{vuln.riskAcceptance.status}</span>
                  </InfoRow>
                  <InfoRow label="Requested">{formatDateTime(vuln.riskAcceptance.requestedAt)}</InfoRow>
                  {vuln.riskAcceptance.expiryDate ? (
                    <InfoRow label="Expires">{formatDateTime(vuln.riskAcceptance.expiryDate)}</InfoRow>
                  ) : null}
                  <div className="px-3 py-2">
                    <span className="text-xs text-muted-foreground">Justification</span>
                    <p className="mt-1 text-sm">{vuln.riskAcceptance.justification}</p>
                  </div>
                  {vuln.riskAcceptance.conditions ? (
                    <div className="px-3 py-2">
                      <span className="text-xs text-muted-foreground">Conditions</span>
                      <p className="mt-1 text-sm">{vuln.riskAcceptance.conditions}</p>
                    </div>
                  ) : null}
                </div>
              </section>
            ) : null}
          </div>
        ) : null}
      </SheetContent>
    </Sheet>
  )
}
