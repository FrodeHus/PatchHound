import type { DecisionVuln } from '@/api/remediation.schemas'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { toneBadge } from '@/lib/tone-classes'
import { severityTone, outcomeLabel, outcomeTone } from './remediation-utils'

type RemediationVulnDrawerProps = {
  vuln: DecisionVuln | null
  isOpen: boolean
  onOpenChange: (open: boolean) => void
}

function InfoRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-4 py-1.5 px-3">
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
                    {vuln.effectiveSeverity ?? vuln.vendorSeverity}
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
            <section className="space-y-1">
              <h4 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Threat Intelligence</h4>
              <div className="divide-y divide-border/50 rounded-lg border border-border/70 bg-background">
                <InfoRow label="EPSS Score">
                  {vuln.epssScore != null
                    ? <span className="tabular-nums">{(vuln.epssScore * 100).toFixed(2)}%</span>
                    : <span className="text-muted-foreground">-</span>}
                </InfoRow>
                <InfoRow label="Known Exploited (KEV)">
                  {vuln.knownExploited
                    ? <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-medium ${toneBadge('danger')}`}>Yes</span>
                    : 'No'}
                </InfoRow>
                <InfoRow label="Public Exploit">
                  {vuln.publicExploit
                    ? <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-medium ${toneBadge('warning')}`}>Yes</span>
                    : 'No'}
                </InfoRow>
                <InfoRow label="Active Alert">
                  {vuln.activeAlert
                    ? <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-medium ${toneBadge('warning')}`}>Yes</span>
                    : 'No'}
                </InfoRow>
                <InfoRow label="Episode Risk Score">
                  {vuln.episodeRiskScore != null
                    ? <span className="tabular-nums font-medium">{vuln.episodeRiskScore.toFixed(0)}</span>
                    : <span className="text-muted-foreground">-</span>}
                </InfoRow>
              </div>
            </section>

            {/* Override */}
            {vuln.overrideOutcome ? (
              <section className="space-y-1">
                <h4 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Vulnerability Override</h4>
                <div className="rounded-lg border border-border/70 bg-background p-3">
                  <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-medium ${toneBadge(outcomeTone(vuln.overrideOutcome))}`}>
                    {outcomeLabel(vuln.overrideOutcome)}
                  </span>
                </div>
              </section>
            ) : null}
          </div>
        ) : null}
      </SheetContent>
    </Sheet>
  )
}
