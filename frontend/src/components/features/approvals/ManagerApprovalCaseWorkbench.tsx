import { useState } from 'react'
import { Link } from '@tanstack/react-router'
import { CheckCircle, ChevronDown, ChevronUp, ExternalLink, SearchCheck, ShieldCheck, XCircle } from 'lucide-react'
import type { DecisionContext, DecisionVuln } from '@/api/remediation.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import {
  outcomeLabel,
  outcomeTone,
  severityTone,
} from '@/components/features/remediation/remediation-utils'
import { toneBadge } from '@/lib/tone-classes'
import { formatNullableDateTime, startCase } from '@/lib/formatting'
import { cn } from '@/lib/utils'

type ManagerRole = 'security-manager' | 'technical-manager'

type Props = {
  data: DecisionContext
  caseId: string
  role: ManagerRole
  isSubmitting?: boolean
  error?: string | null
  onResolve: (action: 'approve' | 'reject', justification?: string, maintenanceWindowDate?: string) => void
}

const roleCopy: Record<ManagerRole, { eyebrow: string; title: string; description: string }> = {
  'security-manager': {
    eyebrow: 'Security manager workbench',
    title: 'Security approval',
    description: 'Review the owner decision, validate the justification, and approve or return the remediation posture.',
  },
  'technical-manager': {
    eyebrow: 'Technical manager workbench',
    title: 'Technical approval',
    description: 'Confirm the patching decision is ready to move into execution and set the maintenance window before approval.',
  },
}

function toDateInputValue(value?: string | null) {
  if (!value) return ''
  return value.slice(0, 10)
}

function toIsoDateBoundary(value: string) {
  return value ? `${value}T00:00:00Z` : undefined
}

export function ManagerApprovalCaseWorkbench({
  data,
  caseId,
  role,
  isSubmitting,
  error,
  onResolve,
}: Props) {
  const [justification, setJustification] = useState('')
  const [maintenanceWindowDate, setMaintenanceWindowDate] = useState(
    toDateInputValue(data.currentDecision?.maintenanceWindowDate)
  )
  const [attemptedAction, setAttemptedAction] = useState<'approve' | 'reject' | null>(null)
  const [showVulnerabilities, setShowVulnerabilities] = useState(false)
  const copy = roleCopy[role]
  const decision = data.currentDecision
  const recommendation = data.recommendations[0] ?? null
  const vulnerabilities = data.openVulnerabilities.length > 0 ? data.openVulnerabilities : data.topVulnerabilities
  const isPatchingApproval = decision?.outcome === 'ApprovedForPatching'
  const requiresDescription = role === 'security-manager' || attemptedAction === 'reject'
  const showJustificationError = Boolean(attemptedAction && requiresDescription && !justification.trim())
  const showMaintenanceError = Boolean(attemptedAction === 'approve' && isPatchingApproval && !maintenanceWindowDate)
  const descriptionErrorText = role === 'security-manager'
    ? 'Justification is required for this approval path.'
    : 'Description is required for this approval path.'

  function resolve(action: 'approve' | 'reject') {
    setAttemptedAction(action)
    if ((role === 'security-manager' || action === 'reject') && !justification.trim()) return
    if (action === 'approve' && isPatchingApproval && !maintenanceWindowDate) return

    onResolve(
      action,
      justification.trim() || undefined,
      action === 'approve' ? toIsoDateBoundary(maintenanceWindowDate) : undefined
    )
  }

  return (
    <section className="space-y-5">
      <header className="rounded-lg border border-border/70 bg-card px-5 py-4">
        <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
          <div className="min-w-0 space-y-2">
            <p className="text-xs uppercase tracking-[0.16em] text-muted-foreground">
              {copy.eyebrow}
            </p>
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-2xl font-semibold leading-tight">
                {copy.title}: {startCase(data.softwareName)}
              </h1>
              <Badge variant="outline">{data.workflowState.currentStageLabel}</Badge>
            </div>
            <p className="max-w-4xl text-sm leading-relaxed text-muted-foreground">
              {copy.description}
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
        <Metric label="Open vulnerabilities" value={data.summary.totalVulnerabilities.toLocaleString()} detail={`${data.summary.criticalCount} critical, ${data.summary.highCount} high`} />
        <Metric label="Affected devices" value={data.workflow.affectedDeviceCount.toLocaleString()} detail={`${data.workflow.affectedOwnerTeamCount} owner teams`} />
        <Metric label="Highest criticality" value={data.criticality} detail={`${data.workflowState.currentActorSummary}`} />
        <Metric label="SLA" value={data.sla?.slaStatus ?? 'Not set'} detail={data.sla?.dueDate ? `Due ${formatNullableDateTime(data.sla.dueDate)}` : 'No due date'} />
      </div>

      <section className="grid gap-4 xl:grid-cols-[minmax(0,0.9fr)_minmax(420px,1.1fr)]">
        <div className="space-y-4">
          <div className="rounded-lg border border-border/70 bg-card">
            <div className="border-b border-border/70 px-4 py-3">
              <h2 className="flex items-center gap-2 text-base font-semibold">
                <SearchCheck className="size-4 text-primary" />
                Security analyst input
              </h2>
            </div>
            <div className="space-y-4 p-4">
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
                  <p className="whitespace-pre-line text-sm leading-relaxed text-foreground">
                    {recommendation.rationale}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {recommendation.analystDisplayName ? `${recommendation.analystDisplayName}, ` : ''}
                    {formatNullableDateTime(recommendation.createdAt)}
                  </p>
                </>
              ) : (
                <p className="text-sm text-muted-foreground">No analyst recommendation has been captured for this case.</p>
              )}
            </div>
          </div>

          <div className="rounded-lg border border-border/70 bg-card">
            <div className="border-b border-border/70 px-4 py-3">
              <h2 className="text-base font-semibold">Owner decision</h2>
            </div>
            <div className="space-y-4 p-4">
              {decision ? (
                <>
                  <div className="flex flex-wrap items-center gap-2">
                    <span className={cn('inline-flex rounded-full border px-2 py-0.5 text-xs font-medium', toneBadge(outcomeTone(decision.outcome)))}>
                      {outcomeLabel(decision.outcome)}
                    </span>
                    <span className={cn('inline-flex rounded-full border px-2 py-0.5 text-xs font-medium', toneBadge(severityTone(data.criticality)))}>
                      {data.criticality}
                    </span>
                  </div>
                  <div>
                    <p className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Owner justification</p>
                    <p className="mt-2 whitespace-pre-line text-sm leading-relaxed text-foreground">
                      {decision.justification || 'No owner justification was recorded.'}
                    </p>
                  </div>
                  <div className="grid gap-3 text-sm text-muted-foreground sm:grid-cols-2">
                    <DecisionMeta label="Decided" value={formatNullableDateTime(decision.decidedAt)} />
                    <DecisionMeta label="Maintenance window" value={decision.maintenanceWindowDate ? formatNullableDateTime(decision.maintenanceWindowDate) : 'Not scheduled'} />
                    <DecisionMeta label="Expiry" value={decision.expiryDate ? formatNullableDateTime(decision.expiryDate) : 'No expiry'} />
                    <DecisionMeta label="Review date" value={decision.reEvaluationDate ? formatNullableDateTime(decision.reEvaluationDate) : 'No review date'} />
                  </div>
                </>
              ) : (
                <p className="text-sm text-muted-foreground">No pending decision is available for approval.</p>
              )}
            </div>
          </div>
        </div>

        <div className="rounded-lg border border-border/70 bg-card">
          <div className="border-b border-border/70 px-4 py-3">
            <h2 className="flex items-center gap-2 text-base font-semibold">
              <ShieldCheck className="size-4 text-primary" />
              Decision point
            </h2>
          </div>
          <div className="space-y-4 p-4">
            {isPatchingApproval ? (
              <div className="space-y-2">
                <label className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                  Maintenance window date
                </label>
                <Input
                  type="date"
                  value={maintenanceWindowDate}
                  onChange={(event) => setMaintenanceWindowDate(event.target.value)}
                  aria-invalid={showMaintenanceError}
                />
                {showMaintenanceError ? (
                  <p className="text-xs text-destructive">Set a maintenance window before approving patching.</p>
                ) : null}
              </div>
            ) : null}

            <div className="space-y-2">
              <div className="flex items-center justify-between gap-3">
                <label className="text-xs font-medium uppercase tracking-[0.14em] text-muted-foreground">
                  Approval justification
                </label>
                {requiresDescription ? (
                  <span className="rounded-full border border-border/70 bg-background px-2 py-0.5 text-[11px] text-muted-foreground">
                    Required
                  </span>
                ) : null}
              </div>
              <Textarea
                value={justification}
                onChange={(event) => setJustification(event.target.value)}
                placeholder="Record the approval or rejection rationale"
                aria-invalid={showJustificationError}
              />
              {showJustificationError ? (
                <p className="text-xs text-destructive">{descriptionErrorText}</p>
              ) : null}
            </div>

            {error ? (
              <div className="rounded-md border border-destructive/35 bg-destructive/8 px-3 py-2 text-sm text-destructive">
                {error}
              </div>
            ) : null}

            <div className="flex flex-wrap justify-end gap-2">
              <Button
                type="button"
                variant="outline"
                disabled={!decision || isSubmitting}
                onClick={() => resolve('reject')}
              >
                <XCircle className="size-4" />
                Return decision
              </Button>
              <Button
                type="button"
                disabled={!decision || isSubmitting}
                onClick={() => resolve('approve')}
              >
                <CheckCircle className="size-4" />
                Approve
              </Button>
            </div>
          </div>
        </div>
      </section>

      <section className="rounded-lg border border-border/70 bg-card">
        <div className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <h2 className="text-base font-semibold">Technical vulnerability details</h2>
            <p className="text-sm text-muted-foreground">
              Hidden by default. Open only if the approval decision needs more technical context.
            </p>
          </div>
          <Button
            type="button"
            variant="outline"
            aria-expanded={showVulnerabilities}
            onClick={() => setShowVulnerabilities((value) => !value)}
          >
            {showVulnerabilities ? <ChevronUp className="size-4" /> : <ChevronDown className="size-4" />}
            {showVulnerabilities ? 'Hide vulnerabilities' : 'Show vulnerabilities'}
          </Button>
        </div>
        {showVulnerabilities ? (
          <div className="overflow-x-auto border-t border-border/70">
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
                {vulnerabilities.slice(0, 8).map((vulnerability) => (
                  <VulnerabilityRow key={vulnerability.vulnerabilityId} vulnerability={vulnerability} />
                ))}
                {vulnerabilities.length === 0 ? (
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

function DecisionMeta({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-xs uppercase tracking-[0.14em]">{label}</p>
      <p className="mt-1 text-foreground">{value}</p>
    </div>
  )
}

function Metric({ label, value, detail }: { label: string; value: string; detail: string }) {
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
    <tr className="align-top">
      <td className="px-4 py-3">
        <div className="font-medium">{vulnerability.externalId}</div>
        <div className="line-clamp-1 max-w-xl text-xs text-muted-foreground">{vulnerability.title}</div>
      </td>
      <td className="px-4 py-3">
        <span className={cn('inline-flex rounded-full border px-2 py-0.5 text-xs font-medium', toneBadge(severityTone(vulnerability.effectiveSeverity ?? vulnerability.vendorSeverity)))}>
          {vulnerability.effectiveSeverity ?? vulnerability.vendorSeverity}
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
