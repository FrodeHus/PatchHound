import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { ArrowLeft, Ban, CheckCircle, Clock, ShieldAlert, XCircle } from 'lucide-react'
import type { DecisionContext, DecisionVuln } from '@/api/remediation.schemas'
import { approveOrRejectDecision } from '@/api/remediation.functions'
import { fetchDecisionAuditTrail } from '@/api/approval-tasks.functions'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { toneBadge } from '@/lib/tone-classes'
import { formatDateTime, formatNullableDateTime } from '@/lib/formatting'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { softwareQueryKeys } from '@/features/software/list-state'
import { RemediationSummaryCards } from './RemediationSummaryCards'
import { RemediationVulnTable } from './RemediationVulnTable'
import { RemediationVulnDrawer } from './RemediationVulnDrawer'
import { DecisionForm } from './DecisionForm'
import { RecommendationPanel } from './RecommendationPanel'
import { SlaIndicator } from './SlaIndicator'
import {
  outcomeLabel,
  outcomeTone,
  approvalStatusTone,
  riskBandTone,
} from './remediation-utils'

type SoftwareRemediationViewProps = {
  data: DecisionContext
  tenantSoftwareId: string
  embedded?: boolean
}

export function SoftwareRemediationView({ data, tenantSoftwareId, embedded = false }: SoftwareRemediationViewProps) {
  const [selectedVuln, setSelectedVuln] = useState<DecisionVuln | null>(null)
  const { selectedTenantId } = useTenantScope()
  const queryClient = useQueryClient()
  const queryKey = softwareQueryKeys.remediation(selectedTenantId, tenantSoftwareId)

  const [approving, setApproving] = useState(false)

  async function handleApproveReject(action: 'approve' | 'reject' | 'cancel') {
    if (!data.currentDecision) return
    setApproving(true)
    try {
      await approveOrRejectDecision({
        data: {
          tenantSoftwareId,
          decisionId: data.currentDecision.id,
          action,
        },
      })
      await queryClient.invalidateQueries({ queryKey })
    } finally {
      setApproving(false)
    }
  }

  return (
    <section className="space-y-5">
      {/* Header — hidden when embedded in the software detail page */}
      {!embedded ? (
        <header className="flex flex-wrap items-center gap-3">
          <Link
            to="/software/$id"
            params={{ id: tenantSoftwareId }}
            search={{ page: 1, pageSize: 25, version: '', tab: 'remediation' }}
            className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground"
          >
            <ArrowLeft className="size-4" />
            Back to software
          </Link>
          <div className="flex items-center gap-2">
            <h1 className="text-2xl font-semibold tracking-tight">{data.assetName}</h1>
            <span className="rounded-full border border-border/70 bg-muted px-2.5 py-0.5 text-xs font-medium text-muted-foreground">
              {data.criticality} criticality
            </span>
            {data.riskScore ? (
              <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-semibold ${toneBadge(riskBandTone(data.riskScore.riskBand))}`}>
                {data.riskScore.riskBand} ({data.riskScore.compositeScore.toFixed(0)})
              </span>
            ) : null}
          </div>
        </header>
      ) : null}

      {/* SLA Indicator */}
      {data.sla ? <SlaIndicator sla={data.sla} /> : null}

      {/* Executive Summary */}
      <RemediationSummaryCards summary={data.summary} />

      {/* AI Narrative */}
      {data.aiNarrative ? (
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm">AI Analysis</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-sm leading-relaxed text-muted-foreground whitespace-pre-line">
              {data.aiNarrative}
            </p>
          </CardContent>
        </Card>
      ) : null}

      {/* Analyst Recommendations */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm">Analyst Recommendations</CardTitle>
        </CardHeader>
        <CardContent>
          <RecommendationPanel
            tenantSoftwareId={tenantSoftwareId}
            recommendations={data.recommendations}
            queryKey={queryKey}
          />
        </CardContent>
      </Card>

      {/* Current Decision */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm">Current Decision</CardTitle>
        </CardHeader>
        <CardContent>
          {data.currentDecision ? (
            <div className="space-y-3">
              <div className="flex flex-wrap items-center gap-2">
                <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-medium ${toneBadge(outcomeTone(data.currentDecision.outcome))}`}>
                  {outcomeLabel(data.currentDecision.outcome)}
                </span>
                <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-medium ${toneBadge(approvalStatusTone(data.currentDecision.approvalStatus))}`}>
                  {data.currentDecision.approvalStatus}
                </span>
              </div>

              {data.currentDecision.justification ? (
                <div>
                  <p className="text-xs text-muted-foreground">Justification</p>
                  <p className="mt-1 text-sm">{data.currentDecision.justification}</p>
                </div>
              ) : null}

              <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-xs text-muted-foreground sm:grid-cols-4">
                <div>
                  <span className="block font-medium text-foreground">Decided</span>
                  {formatDateTime(data.currentDecision.decidedAt)}
                </div>
                {data.currentDecision.approvedAt ? (
                  <div>
                    <span className="block font-medium text-foreground">Approved</span>
                    {formatDateTime(data.currentDecision.approvedAt)}
                  </div>
                ) : null}
                {data.currentDecision.expiryDate ? (
                  <div>
                    <span className="block font-medium text-foreground">Expires</span>
                    {formatNullableDateTime(data.currentDecision.expiryDate)}
                  </div>
                ) : null}
                {data.currentDecision.reEvaluationDate ? (
                  <div>
                    <span className="block font-medium text-foreground">Re-evaluate</span>
                    {formatNullableDateTime(data.currentDecision.reEvaluationDate)}
                  </div>
                ) : null}
              </div>

              {/* Action buttons */}
              <div className="flex gap-2 pt-1">
                {data.currentDecision.approvalStatus === 'PendingApproval' ? (
                  <>
                    <Button
                      size="sm"
                      onClick={() => handleApproveReject('approve')}
                      disabled={approving}
                    >
                      <CheckCircle className="mr-1.5 size-3.5" />
                      Approve
                    </Button>
                    <Button
                      variant="destructive"
                      size="sm"
                      onClick={() => handleApproveReject('reject')}
                      disabled={approving}
                    >
                      <XCircle className="mr-1.5 size-3.5" />
                      Reject
                    </Button>
                  </>
                ) : null}
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => handleApproveReject('cancel')}
                  disabled={approving}
                >
                  <Ban className="mr-1.5 size-3.5" />
                  Cancel Decision
                </Button>
              </div>

              {/* Overrides */}
              {data.currentDecision.overrides.length > 0 ? (
                <div className="pt-2">
                  <p className="text-xs font-medium text-muted-foreground mb-1.5">
                    Vulnerability Overrides ({data.currentDecision.overrides.length})
                  </p>
                  <div className="space-y-1">
                    {data.currentDecision.overrides.map((ov) => (
                      <div
                        key={ov.id}
                        className="flex items-center justify-between rounded border border-border/70 bg-background px-3 py-1.5 text-xs"
                      >
                        <span className={`inline-flex rounded-full border px-2 py-0.5 font-medium ${toneBadge(outcomeTone(ov.outcome))}`}>
                          {outcomeLabel(ov.outcome)}
                        </span>
                        <span className="text-muted-foreground">{ov.justification}</span>
                      </div>
                    ))}
                  </div>
                </div>
              ) : null}
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">
              No software-wide remediation decision has been made yet. Use the form below to create one for every active version cohort.
            </p>
          )}
        </CardContent>
      </Card>

      {/* Approval History */}
      {data.currentDecision ? (
        <ApprovalHistorySection tenantSoftwareId={tenantSoftwareId} decisionId={data.currentDecision.id} />
      ) : null}

      {/* Decision Form — only when no active decision exists */}
      {!data.currentDecision ? (
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm">New Decision</CardTitle>
          </CardHeader>
          <CardContent>
            <DecisionForm tenantSoftwareId={tenantSoftwareId} queryKey={queryKey} />
          </CardContent>
        </Card>
      ) : null}

      {/* Vulnerabilities */}
      <div>
        <h2 className="mb-3 text-lg font-semibold">Top vulnerabilities across this software</h2>
        <RemediationVulnTable
          vulnerabilities={data.topVulnerabilities}
          decisionId={data.currentDecision?.id ?? null}
          tenantSoftwareId={tenantSoftwareId}
          queryKey={queryKey}
          onSelectVuln={setSelectedVuln}
        />
      </div>

      <RemediationVulnDrawer
        vuln={selectedVuln}
        isOpen={selectedVuln !== null}
        onOpenChange={(open) => { if (!open) setSelectedVuln(null) }}
      />
    </section>
  )
}

function auditActionIcon(action: string) {
  switch (action) {
    case 'Approved':
      return <CheckCircle className="size-4 text-tone-success-foreground" />
    case 'Denied':
      return <XCircle className="size-4 text-tone-danger-foreground" />
    case 'AutoDenied':
      return <Clock className="size-4 text-muted-foreground" />
    case 'Created':
      return <ShieldAlert className="size-4 text-primary" />
    default:
      return <Clock className="size-4 text-muted-foreground" />
  }
}

function ApprovalHistorySection({ tenantSoftwareId, decisionId }: { tenantSoftwareId: string; decisionId: string }) {
  const auditQuery = useQuery({
    queryKey: ['decision-audit-trail', tenantSoftwareId, decisionId],
    queryFn: () => fetchDecisionAuditTrail({ data: { tenantSoftwareId, decisionId } }),
  })

  if (!auditQuery.data || auditQuery.data.length === 0) return null

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm">Approval History</CardTitle>
      </CardHeader>
      <CardContent>
        <div className="space-y-0">
          {auditQuery.data.map((entry, i) => (
            <div
              key={`${entry.timestamp}-${i}`}
              className="flex gap-3 border-l-2 border-border/60 py-3 pl-4"
            >
              <div className="mt-0.5">{auditActionIcon(entry.action)}</div>
              <div className="min-w-0 flex-1 space-y-0.5">
                <p className="text-sm">
                  <span className="font-medium">
                    {entry.userDisplayName ?? 'System'}
                  </span>{' '}
                  <span className="text-muted-foreground">
                    {entry.action.toLowerCase()}
                  </span>
                </p>
                {entry.justification ? (
                  <p className="text-sm italic text-muted-foreground">
                    &ldquo;{entry.justification}&rdquo;
                  </p>
                ) : null}
                <p className="text-[10px] text-muted-foreground">
                  {formatDateTime(entry.timestamp)}
                </p>
              </div>
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  )
}
