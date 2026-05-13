import { useId, useMemo, useState } from 'react'
import { Link } from '@tanstack/react-router'
import { ChevronDown, ChevronUp, ExternalLink, SearchCheck, ShieldAlert, TriangleAlert } from 'lucide-react'
import type { DecisionContext, DecisionVuln } from '@/api/remediation.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { formatDateTime, formatNullableDateTime, startCase } from '@/lib/formatting'
import { toneBadge } from '@/lib/tone-classes'
import { cn } from '@/lib/utils'
import {
  formatSoftwareOwnerRoutingDetail,
  outcomeLabel,
  outcomeTone,
  severityTone,
} from './remediation-utils'
import { DecisionForm } from './DecisionForm'

type AssetOwnerWorkbenchProps = {
  data: DecisionContext
  caseId: string
  queryKey: readonly unknown[]
}

export function AssetOwnerWorkbench({ data, caseId, queryKey }: AssetOwnerWorkbenchProps) {
  const vulnerabilityDetailsId = useId()
  const [showVulnerabilities, setShowVulnerabilities] = useState(false)
  const recommendation = data.recommendations[0] ?? null
  const vulnerabilities = data.openVulnerabilities.length > 0 ? data.openVulnerabilities : data.topVulnerabilities
  const visibleVulnerabilities = useMemo(() => vulnerabilities.slice(0, 8), [vulnerabilities])
  const rejection = data.latestApprovalResolution?.status === 'Rejected'
    ? data.latestApprovalResolution
    : null
  const displaySoftwareName = startCase(data.softwareName)
  const softwareIdentity = data.softwareVendor
    && !displaySoftwareName.toLowerCase().startsWith(data.softwareVendor.toLowerCase())
    ? `${data.softwareVendor} ${displaySoftwareName}`
    : displaySoftwareName

  return (
    <section className="space-y-5">
      <header className="rounded-lg border border-border/70 bg-card px-5 py-4">
        <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
          <div className="min-w-0 space-y-2">
            <p className="text-xs uppercase tracking-[0.16em] text-muted-foreground">
              Asset owner workbench
            </p>
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-2xl font-semibold leading-tight">{softwareIdentity}</h1>
              {data.softwareCategory ? <Badge variant="outline">{data.softwareCategory}</Badge> : null}
              <Badge variant="outline">{data.workflowState.currentStageLabel}</Badge>
            </div>
            <p className="max-w-4xl text-sm leading-relaxed text-muted-foreground">
              {data.softwareDescription
                ?? 'Review the analyst recommendation, choose the remediation posture, and send the decision into the existing approval workflow.'}
            </p>
          </div>
          <Link
            to="/remediation/cases/$caseId"
            params={{ caseId }}
            className="inline-flex h-9 items-center gap-2 self-start rounded-md border border-border bg-background px-3 text-sm font-medium hover:bg-muted"
          >
            Full case
            <ExternalLink className="size-3.5" />
          </Link>
        </div>
      </header>

      <div className="grid gap-3 md:grid-cols-4">
        <WorkbenchMetric label="Open vulnerabilities" value={data.summary.totalVulnerabilities.toLocaleString()} detail={`${data.summary.criticalCount} critical, ${data.summary.highCount} high`} />
        <WorkbenchMetric label="Affected devices" value={data.workflow.affectedDeviceCount.toLocaleString()} detail={`${data.workflow.affectedOwnerTeamCount} owner teams`} />
        <WorkbenchMetric label="Owner routing" value={data.softwareOwnerTeamName ?? 'Default Team'} detail={formatSoftwareOwnerRoutingDetail(data.softwareOwnerTeamName, data.softwareOwnerAssignmentSource)} />
        <WorkbenchMetric label="SLA" value={data.sla?.slaStatus ?? 'Not set'} detail={data.sla?.dueDate ? `Due ${formatNullableDateTime(data.sla.dueDate)}` : 'No due date'} />
      </div>

      {rejection ? (
        <div className="rounded-lg border border-destructive/35 bg-destructive/8 px-4 py-3">
          <div className="flex items-start gap-3">
            <TriangleAlert className="mt-0.5 size-4 shrink-0 text-destructive" />
            <div className="space-y-1">
              <p className="text-sm font-medium">Previous decision was returned</p>
              <p className="text-sm text-muted-foreground">{rejection.justification ?? 'No rejection note was provided.'}</p>
              <p className="text-xs text-muted-foreground">
                {rejection.resolvedByDisplayName ? `${rejection.resolvedByDisplayName}, ` : ''}
                {formatNullableDateTime(rejection.resolvedAt)}
              </p>
            </div>
          </div>
        </div>
      ) : null}

      <div className="grid gap-4 xl:grid-cols-[minmax(0,0.95fr)_minmax(420px,1.05fr)]">
        <Card className="shadow-none">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <SearchCheck className="size-4 text-primary" />
              Analyst recommendation
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            {recommendation ? (
              <>
                <div className="flex flex-wrap items-center gap-2">
                  <span className={cn('inline-flex rounded-full border px-2 py-0.5 text-xs font-medium', toneBadge(outcomeTone(recommendation.recommendedOutcome)))}>
                    {outcomeLabel(recommendation.recommendedOutcome)}
                  </span>
                  {recommendation.priorityOverride ? (
                    <span className="text-xs text-muted-foreground">{recommendation.priorityOverride} priority</span>
                  ) : null}
                </div>
                <p className="whitespace-pre-line text-sm leading-relaxed text-muted-foreground">
                  {recommendation.rationale}
                </p>
                <p className="text-xs text-muted-foreground">
                  {recommendation.analystDisplayName ? `${recommendation.analystDisplayName}, ` : ''}
                  {formatDateTime(recommendation.createdAt)}
                </p>
              </>
            ) : (
              <p className="text-sm text-muted-foreground">
                No analyst recommendation has been captured yet. Use the full case if the workflow should go back to security analysis.
              </p>
            )}
          </CardContent>
        </Card>

        <Card className="shadow-none">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <ShieldAlert className="size-4 text-primary" />
              Owner decision
            </CardTitle>
          </CardHeader>
          <CardContent>
            <DecisionForm
              caseId={caseId}
              queryKey={queryKey}
              initialOutcome={recommendation?.recommendedOutcome}
              submitLabel="Submit owner decision"
            />
          </CardContent>
        </Card>
      </div>


      <section className="rounded-lg border border-border/70 bg-card">
        <div className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h2 className="text-base font-semibold">Technical vulnerability details</h2>
            <p className="text-sm text-muted-foreground">
              Hidden by default. Open only when you need to inspect the detailed exposure drivers.
            </p>
          </div>
          <Button
            type="button"
            variant="outline"
            aria-controls={vulnerabilityDetailsId}
            aria-expanded={showVulnerabilities}
            onClick={() => setShowVulnerabilities((value) => !value)}
          >
            {showVulnerabilities ? <ChevronUp className="size-4" /> : <ChevronDown className="size-4" />}
            {showVulnerabilities ? 'Hide vulnerabilities' : 'Show vulnerabilities'}
          </Button>
        </div>
        {showVulnerabilities ? (
          <div id={vulnerabilityDetailsId} className="overflow-x-auto border-t border-border/70">
            <table className="min-w-[760px] divide-y divide-border/70 text-sm">
              <thead className="bg-muted/35 text-left text-xs uppercase tracking-[0.12em] text-muted-foreground">
                <tr>
                  <th className="px-4 py-3">Vulnerability</th>
                  <th className="px-4 py-3">Severity</th>
                  <th className="px-4 py-3">Threats</th>
                  <th className="px-4 py-3">Scope</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border/60">
                {visibleVulnerabilities.map((vulnerability) => (
                  <VulnerabilityRow key={vulnerability.vulnerabilityId} vulnerability={vulnerability} />
                ))}
                {visibleVulnerabilities.length === 0 ? (
                  <tr>
                    <td colSpan={4} className="px-4 py-8 text-center text-muted-foreground">
                      No open vulnerabilities are linked to this remediation case.
                    </td>
                  </tr>
                ) : null}
              </tbody>
            </table>
          </div>
        ) : null}
      </section>
    </section>
  )
}

function WorkbenchMetric({ label, value, detail }: { label: string; value: string; detail: string }) {
  return (
    <div className="rounded-lg border border-border/70 bg-card px-4 py-3">
      <p className="text-xs uppercase tracking-[0.14em] text-muted-foreground">{label}</p>
      <p className="mt-1 truncate text-lg font-semibold">{value}</p>
      <p className="mt-1 truncate text-xs text-muted-foreground">{detail}</p>
    </div>
  )
}

function VulnerabilityRow({ vulnerability }: { vulnerability: DecisionVuln }) {
  return (
    <tr className="align-top transition-colors hover:bg-muted/25">
      <td className="px-4 py-3">
        <div className="font-medium">{vulnerability.externalId}</div>
        <div className="line-clamp-1 max-w-xl text-xs text-muted-foreground">{vulnerability.title}</div>
      </td>
      <td className="px-4 py-3">
        <span className={cn('inline-flex rounded-full border px-2 py-0.5 text-xs font-medium', toneBadge(severityTone(vulnerability.effectiveSeverity ?? vulnerability.vendorSeverity)))}>
          {vulnerability.effectiveSeverity ?? vulnerability.vendorSeverity}
          {vulnerability.effectiveScore != null ? ` ${vulnerability.effectiveScore.toFixed(1)}` : ''}
        </span>
      </td>
      <td className="px-4 py-3 text-xs text-muted-foreground">
        {[
          vulnerability.knownExploited ? 'KEV' : null,
          vulnerability.publicExploit ? 'Exploit' : null,
          vulnerability.activeAlert ? 'Alert' : null,
        ].filter(Boolean).join(', ') || 'None'}
      </td>
      <td className="px-4 py-3 text-muted-foreground">
        {vulnerability.affectedDeviceCount.toLocaleString()} devices
      </td>
    </tr>
  )
}
