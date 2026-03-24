import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { ArrowLeft, Ban, CheckCircle, XCircle } from 'lucide-react'
import type { DecisionContext, DecisionVuln } from '@/api/remediation.schemas'
import { approveOrRejectDecision } from '@/api/remediation.functions'
import { fetchDecisionAuditTrail } from '@/api/approval-tasks.functions'
import {
  AuditTimeline,
  type AuditTimelineEvent,
} from '@/components/features/audit/AuditTimeline'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { toneBadge } from '@/lib/tone-classes'
import { formatDateTime, formatNullableDateTime, startCase } from '@/lib/formatting'
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
  approvalStatusLabel,
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
      {!embedded ? (
        <header className="rounded-[28px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_52%),var(--color-card)] p-4">
          <div className="flex flex-col gap-3 xl:flex-row xl:items-start xl:justify-between">
            <div className="min-w-0 space-y-1.5">
              <Link
                to="/software/$id"
                params={{ id: tenantSoftwareId }}
                search={{ page: 1, pageSize: 25, version: '', tab: 'remediation' }}
                className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground"
              >
                <ArrowLeft className="size-4" />
                Back to software
              </Link>
              <div className="flex flex-wrap items-center gap-2">
                <h1 className="text-[2rem] font-semibold tracking-[-0.05em] leading-none">
                  {startCase(data.softwareName)}
                </h1>
                <span className="rounded-full border border-border/70 bg-background/70 px-2.5 py-0.5 text-xs font-medium text-muted-foreground">
                  {data.criticality} criticality
                </span>
                {data.riskScore ? (
                  <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-semibold ${toneBadge(riskBandTone(data.riskScore.riskBand))}`}>
                    {data.riskScore.riskBand} ({data.riskScore.compositeScore.toFixed(0)})
                  </span>
                ) : null}
                {data.currentDecision ? (
                  <>
                    <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-medium ${toneBadge(outcomeTone(data.currentDecision.outcome))}`}>
                      {outcomeLabel(data.currentDecision.outcome)}
                    </span>
                    <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-medium ${toneBadge(approvalStatusTone(data.currentDecision.approvalStatus))}`}>
                      {approvalStatusLabel(data.currentDecision.approvalStatus)}
                    </span>
                  </>
                ) : null}
              </div>
              <p className="max-w-4xl text-sm leading-snug text-muted-foreground">
                {data.summary.totalVulnerabilities.toLocaleString()} open vulnerabilities across active software scope
                {' · '}
                {data.summary.withKnownExploit > 0
                  ? `${data.summary.withKnownExploit.toLocaleString()} known exploits`
                  : 'No known exploits'}
                {' · '}
                {data.summary.criticalCount + data.summary.highCount} critical or high vulnerabilities
                {data.sla ? ` · SLA ${startCase(data.sla.slaStatus)}` : ''}
              </p>
            </div>
            {data.sla ? (
              <div className="w-full xl:w-auto xl:min-w-[300px]">
                <SlaIndicator sla={data.sla} />
              </div>
            ) : null}
          </div>
        </header>
      ) : null}

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.15fr)_minmax(320px,0.85fr)]">
        <Card className="rounded-[1.6rem] border-border/70">
          <CardHeader className="pb-2">
            <CardTitle className="text-sm">
              {data.currentDecision ? 'Current remediation decision' : 'Make a remediation decision'}
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            {data.currentDecision ? (
              <div className="space-y-4">
                <div className="flex flex-wrap items-center gap-2">
                  <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-medium ${toneBadge(outcomeTone(data.currentDecision.outcome))}`}>
                    {outcomeLabel(data.currentDecision.outcome)}
                  </span>
                <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-medium ${toneBadge(approvalStatusTone(data.currentDecision.approvalStatus))}`}>
                  {approvalStatusLabel(data.currentDecision.approvalStatus)}
                </span>
                </div>

                <div className="rounded-2xl border border-border/70 bg-background/60 p-4">
                  <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">
                    Rationale
                  </p>
                  <p className="mt-2 text-sm leading-relaxed text-foreground/90">
                    {data.currentDecision.justification || 'No justification was provided for this decision.'}
                  </p>
                </div>

                <div className="grid grid-cols-2 gap-3 text-xs text-muted-foreground sm:grid-cols-4">
                  <div className="rounded-xl border border-border/60 bg-background/50 px-3 py-3">
                    <span className="block font-medium text-foreground">Decided</span>
                    {formatDateTime(data.currentDecision.decidedAt)}
                  </div>
                  {data.currentDecision.approvedAt ? (
                    <div className="rounded-xl border border-border/60 bg-background/50 px-3 py-3">
                      <span className="block font-medium text-foreground">Approved</span>
                      {formatDateTime(data.currentDecision.approvedAt)}
                    </div>
                  ) : null}
                  {data.currentDecision.expiryDate ? (
                    <div className="rounded-xl border border-border/60 bg-background/50 px-3 py-3">
                      <span className="block font-medium text-foreground">Expires</span>
                      {formatNullableDateTime(data.currentDecision.expiryDate)}
                    </div>
                  ) : null}
                  {data.currentDecision.reEvaluationDate ? (
                    <div className="rounded-xl border border-border/60 bg-background/50 px-3 py-3">
                      <span className="block font-medium text-foreground">Re-evaluate</span>
                      {formatNullableDateTime(data.currentDecision.reEvaluationDate)}
                    </div>
                  ) : null}
                </div>

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

                {data.currentDecision.overrides.length > 0 ? (
                  <div className="pt-2">
                    <p className="mb-1.5 text-xs font-medium text-muted-foreground">
                      Vulnerability overrides ({data.currentDecision.overrides.length})
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
              <div className="space-y-4">
                <div className="rounded-2xl border border-border/70 bg-background/60 p-4">
                  <p className="text-sm leading-relaxed text-muted-foreground">
                    No remediation decision has been recorded for this software yet. Choose how the organization will handle the current exposure across active version cohorts.
                  </p>
                </div>
                <DecisionForm tenantSoftwareId={tenantSoftwareId} queryKey={queryKey} />
              </div>
            )}
          </CardContent>
        </Card>

        <div className="space-y-4">
          {data.aiNarrative ? (
            <Card className="rounded-[1.6rem] border-border/70">
              <CardHeader className="pb-2">
                <CardTitle className="text-sm">AI analysis</CardTitle>
              </CardHeader>
              <CardContent>
                <p className="whitespace-pre-line text-sm leading-relaxed text-muted-foreground">
                  {data.aiNarrative}
                </p>
              </CardContent>
            </Card>
          ) : null}
        </div>
      </div>

      <RemediationSummaryCards summary={data.summary} />

      <Tabs defaultValue="decision" className="gap-4">
        <TabsList className="h-10 w-full justify-start rounded-xl bg-muted/50 p-1">
          <TabsTrigger value="decision" className="rounded-lg px-4 text-sm">
            Decision
          </TabsTrigger>
          <TabsTrigger value="vulnerabilities" className="rounded-lg px-4 text-sm">
            Vulnerabilities ({data.topVulnerabilities.length})
          </TabsTrigger>
          <TabsTrigger value="history" className="rounded-lg px-4 text-sm">
            History {data.currentDecision || data.recommendations.length > 0 ? `(${(data.currentDecision ? 1 : 0) + data.recommendations.length})` : ''}
          </TabsTrigger>
        </TabsList>

        <TabsContent value="decision" className="space-y-4 pt-1">
          <Card className="rounded-[1.6rem] border-border/70">
            <CardHeader className="pb-2">
              <CardTitle className="text-sm">Analyst recommendations</CardTitle>
            </CardHeader>
            <CardContent>
              {data.recommendations.length === 0 ? (
                <div className="rounded-2xl border border-dashed border-border/70 bg-background/40 px-4 py-4">
                  <p className="text-sm text-muted-foreground">
                    No analyst recommendations have been recorded yet.
                  </p>
                  <div className="mt-3">
                    <RecommendationPanel
                      tenantSoftwareId={tenantSoftwareId}
                      recommendations={data.recommendations}
                      queryKey={queryKey}
                    />
                  </div>
                </div>
              ) : (
                <RecommendationPanel
                  tenantSoftwareId={tenantSoftwareId}
                  recommendations={data.recommendations}
                  queryKey={queryKey}
                />
              )}
            </CardContent>
          </Card>

          {data.aiNarrative ? (
            <Card className="rounded-[1.6rem] border-border/70">
              <CardHeader className="pb-2">
                <CardTitle className="text-sm">AI analysis</CardTitle>
              </CardHeader>
              <CardContent>
                <p className="whitespace-pre-line text-sm leading-relaxed text-muted-foreground">
                  {data.aiNarrative}
                </p>
              </CardContent>
            </Card>
          ) : null}
        </TabsContent>

        <TabsContent value="vulnerabilities" className="space-y-4 pt-1">
          <div>
            <div className="mb-3">
              <h2 className="text-lg font-semibold">Top vulnerabilities driving this decision</h2>
              <p className="text-sm text-muted-foreground">
                Review the highest-priority vulnerabilities across the active software scope before deciding how to handle remediation.
              </p>
            </div>
            <RemediationVulnTable
              vulnerabilities={data.topVulnerabilities}
              decisionId={data.currentDecision?.id ?? null}
              tenantSoftwareId={tenantSoftwareId}
              queryKey={queryKey}
              onSelectVuln={setSelectedVuln}
            />
          </div>
        </TabsContent>

        <TabsContent value="history" className="pt-1">
          <DecisionHistorySection
            tenantSoftwareId={tenantSoftwareId}
            decisionId={data.currentDecision?.id ?? null}
            recommendations={data.recommendations}
          />
        </TabsContent>
      </Tabs>

      <RemediationVulnDrawer
        vuln={selectedVuln}
        isOpen={selectedVuln !== null}
        onOpenChange={(open) => { if (!open) setSelectedVuln(null) }}
      />
    </section>
  )
}

function DecisionHistorySection({
  tenantSoftwareId,
  decisionId,
  recommendations,
}: {
  tenantSoftwareId: string
  decisionId: string | null
  recommendations: DecisionContext['recommendations']
}) {
  const auditQuery = useQuery({
    queryKey: ['decision-audit-trail', tenantSoftwareId, decisionId],
    enabled: Boolean(decisionId),
    queryFn: () => fetchDecisionAuditTrail({ data: { tenantSoftwareId, decisionId: decisionId! } }),
  })

  const recommendationEvents: AuditTimelineEvent[] = recommendations.map((recommendation) => ({
    id: `recommendation-${recommendation.id}`,
    action: 'Recommendation',
    title: `${recommendation.analystDisplayName ?? 'Analyst'} recommended ${outcomeLabel(recommendation.recommendedOutcome).toLowerCase()}.`,
    description: recommendation.rationale,
    timestamp: recommendation.createdAt,
    badges: [
      {
        label: outcomeLabel(recommendation.recommendedOutcome),
        tone: outcomeTone(recommendation.recommendedOutcome),
      },
    ],
  }))

  const auditEvents: AuditTimelineEvent[] = (auditQuery.data ?? []).map((entry, i) => ({
    id: `${entry.timestamp}-${entry.action}-${i}`,
    action: entry.action,
    title: buildDecisionTimelineTitle(entry.action, entry.userDisplayName),
    description: entry.justification
      ? `Justification: ${entry.justification}`
      : undefined,
    timestamp: entry.timestamp,
  }))

  const events = [...recommendationEvents, ...auditEvents]
    .sort((left, right) => new Date(right.timestamp).getTime() - new Date(left.timestamp).getTime())

  return (
    <Card className="rounded-[1.6rem] border-border/70">
      <CardHeader className="pb-2">
        <CardTitle className="text-sm">Decision history</CardTitle>
      </CardHeader>
      <CardContent>
        <AuditTimeline
          events={events}
          emptyMessage="No recommendations or decision events have been recorded yet."
        />
      </CardContent>
    </Card>
  )
}

function buildDecisionTimelineTitle(action: string, userDisplayName: string | null) {
  const actor = userDisplayName ?? 'System'

  switch (action) {
    case 'Created':
      return `${actor} created this remediation decision.`
    case 'Approved':
      return `${actor} approved this remediation decision.`
    case 'Denied':
      return `${actor} denied this remediation decision.`
    case 'AutoDenied':
      return `This remediation decision expired because approval was not completed in time.`
    case 'Expired':
      return `${actor} closed or expired this remediation decision.`
    case 'Updated':
      return `${actor} updated this remediation decision.`
    default:
      return `${actor} changed this remediation decision.`
  }
}
