import { useState, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Ban, CheckCircle, ClipboardCheck, LoaderCircle, SearchCheck, ShieldAlert, Wrench, XCircle } from 'lucide-react'
import {
  fetchTenantSoftwareDetail,
  fetchTenantSoftwareInstallations,
} from '@/api/software.functions'
import type {
  PagedTenantSoftwareInstallations,
  TenantSoftwareDetail,
} from '@/api/software.schemas'
import {
  fetchRemediationTaskTeamStatuses,
} from '@/api/remediation-tasks.functions'
import type { RemediationTaskTeamStatus } from '@/api/remediation-tasks.schemas'
import type { DecisionContext, DecisionVuln } from '@/api/remediation.schemas'
import {
  approveOrRejectDecision,
  generateRemediationAiSummary,
  reviewRemediationAiSummary,
  verifyRecurringRemediation,
} from '@/api/remediation.functions'
import { fetchDecisionAuditTrail } from '@/api/approval-tasks.functions'
import {
  AuditTimeline,
  type AuditTimelineEvent,
} from '@/components/features/audit/AuditTimeline'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { getApiErrorMessage } from '@/lib/api-errors'
import { toneBadge } from '@/lib/tone-classes'
import type { Tone } from '@/lib/tone-classes'
import { formatDateTime, formatNullableDateTime, startCase } from '@/lib/formatting'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { softwareQueryKeys } from '@/features/software/list-state'
import { RemediationSummaryCards } from './RemediationSummaryCards'
import { RemediationVulnTable } from './RemediationVulnTable'
import { RemediationVulnDrawer } from './RemediationVulnDrawer'
import { DecisionForm } from './DecisionForm'
import { RecommendationPanel } from './RecommendationPanel'
import { SlaIndicator } from './SlaIndicator'
import { WorkNotesSheet } from '@/components/features/work-notes/WorkNotesSheet'
import {
  RemediationStageRail,
  type RemediationStage,
  type RemediationStageId,
} from './RemediationStageRail'
import { OpenEpisodeSparkline } from './OpenEpisodeSparkline'
import {
  outcomeLabel,
  outcomeTone,
  approvalStatusLabel,
  approvalStatusTone,
  formatSoftwareOwnerRoutingDetail,
  riskBandTone,
} from './remediation-utils'

type SoftwareRemediationViewProps = {
  data: DecisionContext
  caseId: string
  tenantSoftwareId?: string
  embedded?: boolean
  initialSoftwareDetail?: TenantSoftwareDetail
  initialInstallations?: PagedTenantSoftwareInstallations
  initialDeviceVersion?: string
}

export function SoftwareRemediationView({
  data,
  caseId,
  tenantSoftwareId,
  embedded = false,
  initialSoftwareDetail,
  initialInstallations,
  initialDeviceVersion,
}: SoftwareRemediationViewProps) {
  const [selectedVuln, setSelectedVuln] = useState<DecisionVuln | null>(null)
  const { selectedTenantId } = useTenantScope()
  const queryClient = useQueryClient()
  const queryKey = ['remediation-case', selectedTenantId, caseId]

  const [approving, setApproving] = useState(false)
  const [generatingAiSummary, setGeneratingAiSummary] = useState(false)
  const [recommendationSeed, setRecommendationSeed] = useState<{
    token: number
    outcome?: string | null
    rationale?: string | null
    priorityOverride?: string | null
  } | null>(null)
  const [decisionSeed, setDecisionSeed] = useState<{
    token: number
    outcome?: string | null
    justification?: string | null
    maintenanceWindowDate?: string | null
    expiryDate?: string | null
    reEvaluationDate?: string | null
  } | null>(null)
  const [stageError, setStageError] = useState<string | null>(null)
  const currentStageId = data.workflowState.currentStage as RemediationStageId
  const stages: RemediationStage[] = data.workflowState.stages.map((stage) => ({
    id: stage.id as RemediationStageId,
    label: stage.label,
    state: stage.state as RemediationStage['state'],
    description: stage.description,
  }))

  const softwareDetailQuery = useQuery({
    queryKey: softwareQueryKeys.detail(selectedTenantId, tenantSoftwareId ?? ''),
    queryFn: () => fetchTenantSoftwareDetail({ data: { id: tenantSoftwareId! } }),
    initialData: initialSoftwareDetail,
    enabled: Boolean(tenantSoftwareId),
  })

  const softwareDetail = softwareDetailQuery.data ?? initialSoftwareDetail
  const isComponentSoftware = softwareDetail?.category === 'Component'
  const normalizedDeviceVersion =
    normalizeVersion(initialDeviceVersion ?? softwareDetail?.versionCohorts[0]?.version ?? null)

  const installationsQuery = useQuery({
    queryKey: softwareQueryKeys.installations(selectedTenantId, tenantSoftwareId ?? '', normalizedDeviceVersion, 1, 25),
    queryFn: () => fetchTenantSoftwareInstallations({
      data: {
        id: tenantSoftwareId!,
        version: normalizedDeviceVersion || undefined,
        activeOnly: true,
        page: 1,
        pageSize: 25,
      },
    }),
    enabled: Boolean(softwareDetail) && Boolean(tenantSoftwareId),
    initialData:
      initialInstallations && (initialDeviceVersion ?? '') === normalizedDeviceVersion
        ? initialInstallations
        : undefined,
  })

  const remediationInstallations = installationsQuery.data ?? initialInstallations
  const teamStatusesQuery = useQuery({
    queryKey: ['remediation-team-statuses', selectedTenantId, caseId],
    queryFn: () => fetchRemediationTaskTeamStatuses({ data: { caseId } }),
  })
  const teamStatuses = teamStatusesQuery.data ?? []

  async function handleApproveReject(action: 'approve' | 'reject' | 'cancel', justification?: string, maintenanceWindowDate?: string) {
    if (!data.currentDecision) return
    setApproving(true)
    setStageError(null)
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
    } catch (error) {
      setStageError(getApiErrorMessage(error, 'Unable to update the remediation decision.'))
    } finally {
      setApproving(false)
    }
  }

  async function handleVerification(action: 'keepCurrentDecision' | 'chooseNewDecision') {
    setApproving(true)
    setStageError(null)
    try {
      await verifyRecurringRemediation({
        data: {
          caseId,
          action,
        },
      })
      await queryClient.invalidateQueries({ queryKey })
    } catch (error) {
      setStageError(getApiErrorMessage(error, 'Unable to update the recurrence verification.'))
    } finally {
      setApproving(false)
    }
  }

  async function handleGenerateAiSummary() {
    setGeneratingAiSummary(true)
    setStageError(null)
    try {
      await generateRemediationAiSummary({
        data: {
          caseId,
        },
      })
      await queryClient.invalidateQueries({ queryKey })
    } catch (error) {
      setStageError(getApiErrorMessage(error, 'Unable to generate the AI risk summary.'))
    } finally {
      setGeneratingAiSummary(false)
    }
  }

  async function handleUseAiForAnalystRecommendation() {
    if (!data.aiSummary.analystAssessment && !data.aiSummary.recommendedOutcome && !data.aiSummary.recommendedPriority) {
      return
    }

    setRecommendationSeed({
      token: Date.now(),
      outcome: data.aiSummary.recommendedOutcome || null,
      rationale: data.aiSummary.analystAssessment || null,
      priorityOverride: data.aiSummary.recommendedPriority || null,
    })
    setStageError(null)
    try {
      await reviewRemediationAiSummary({
        data: {
          caseId,
          action: 'edit',
        },
      })
      await queryClient.invalidateQueries({ queryKey })
    } catch (error) {
      setStageError(getApiErrorMessage(error, 'Unable to prepare the AI analyst recommendation.'))
    }
  }

  async function handleUseAiForDecisionForm(source: 'owner' | 'exception') {
    const justification = source === 'exception'
      ? data.aiSummary.exceptionRecommendation || null
      : data.aiSummary.ownerRecommendation || null

    if (!justification && !data.aiSummary.recommendedOutcome) {
      return
    }

    setDecisionSeed({
      token: Date.now(),
      outcome: data.aiSummary.recommendedOutcome || data.currentDecision?.outcome || null,
      justification,
      maintenanceWindowDate: data.currentDecision?.maintenanceWindowDate ?? null,
      expiryDate: data.currentDecision?.expiryDate ?? null,
      reEvaluationDate: data.currentDecision?.reEvaluationDate ?? null,
    })
    setStageError(null)
    try {
      await reviewRemediationAiSummary({
        data: {
          caseId,
          action: 'edit',
        },
      })
      await queryClient.invalidateQueries({ queryKey })
    } catch (error) {
      setStageError(getApiErrorMessage(error, 'Unable to prepare the AI decision draft.'))
    }
  }

  return (
    <section className="space-y-5">
      {!embedded ? (
        <header className="rounded-[28px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_52%),var(--color-card)] p-4">
          <div className="flex flex-col gap-3 xl:flex-row xl:items-start xl:justify-between">
            <div className="min-w-0 space-y-1.5">
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
                {`Owner team ${data.softwareOwnerTeamName ?? 'Default Team'} (${startCase(data.softwareOwnerAssignmentSource)})`}
                {' · '}
                {data.summary.withKnownExploit > 0
                  ? `${data.summary.withKnownExploit.toLocaleString()} known exploits`
                  : 'No known exploits'}
                {' · '}
                {data.workflow.affectedOwnerTeamCount > 0
                  ? `${data.workflow.affectedOwnerTeamCount.toLocaleString()} owner teams affected`
                  : `${data.summary.criticalCount + data.summary.highCount} critical or high vulnerabilities`}
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

      <RemediationStageRail
        stages={stages}
        currentStageId={currentStageId}
      />

      <div className="flex justify-end">
        <WorkNotesSheet
          entityType="remediations"
          entityId={caseId}
          title="Remediation work notes"
          description="Capture tenant-local notes for this remediation workflow."
        />
      </div>

      {isComponentSoftware ? (
        <div className="rounded-[1.25rem] border border-amber-500/25 bg-amber-500/10 px-4 py-3">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="max-w-4xl">
              <p className="text-[11px] uppercase tracking-[0.18em] text-amber-700 dark:text-amber-300">
                Component software
              </p>
              <p className="mt-2 text-sm font-semibold text-foreground">
                This record is categorized as a component, so it usually cannot be patched on its own.
              </p>
              <p className="mt-1.5 text-sm text-muted-foreground">
                The practical remediation path is normally to update the parent software suite or wait for the product maintainer to ship a version that includes the fixed component.
              </p>
            </div>
          </div>
        </div>
      ) : null}

      {data.aiSummary.status === 'Queued' || data.aiSummary.status === 'Generating' ? (
        <div className="flex items-center gap-3 rounded-[1.35rem] border border-border/70 bg-background/65 px-4 py-3">
          <div className="rounded-full border border-primary/20 bg-primary/10 p-2 text-primary">
            <LoaderCircle className="size-4 animate-spin" />
          </div>
          <div className="min-w-0">
            <p className="text-sm font-medium text-foreground">
              AI is working in the background
            </p>
            <p className="text-sm text-muted-foreground">
              The remediation page is ready to use now. Executive, owner, and analyst guidance will appear automatically when the job finishes.
            </p>
          </div>
        </div>
      ) : null}

      {stageError ? (
        <div className="rounded-[1.1rem] border border-destructive/35 bg-destructive/8 px-4 py-3 text-sm text-destructive">
          {stageError}
        </div>
      ) : null}

      <div className="space-y-4">
        {currentStageId === 'remediationDecision' ? (
          <div className="grid gap-4 xl:grid-cols-[minmax(0,1.45fr)_minmax(340px,0.85fr)]">
            <CurrentActionSection
              data={data}
              caseId={caseId}
              queryKey={queryKey}
              currentStageId={currentStageId}
              recommendationSeed={recommendationSeed}
              decisionSeed={decisionSeed}
              approving={approving}
              generatingAiSummary={generatingAiSummary}
              onApproveReject={handleApproveReject}
              onVerify={handleVerification}
              onGenerateAiSummary={handleGenerateAiSummary}
              onUseAnalystRecommendation={handleUseAiForAnalystRecommendation}
              onUseOwnerDecision={async () => handleUseAiForDecisionForm('owner')}
              onUseExceptionDecision={async () => handleUseAiForDecisionForm('exception')}
            />
            <Card className="rounded-[1.8rem] border-border/55 bg-background/40 shadow-none">
              <CardHeader className="space-y-1.5 pb-2">
                <CardTitle className="text-xs uppercase tracking-[0.16em] text-muted-foreground">Security recommendation</CardTitle>
                <p className="text-sm leading-relaxed text-muted-foreground">
                  Security analysis to help the asset owner decide. Advisory only.
                </p>
              </CardHeader>
              <CardContent>
                <SecurityRecommendationSidecar
                  recommendation={data.recommendations[0] ?? null}
                />
              </CardContent>
            </Card>
          </div>
        ) : (
          <CurrentActionSection
            data={data}
            caseId={caseId}
            queryKey={queryKey}
            currentStageId={currentStageId}
            recommendationSeed={recommendationSeed}
            decisionSeed={decisionSeed}
            approving={approving}
            generatingAiSummary={generatingAiSummary}
            onApproveReject={handleApproveReject}
            onVerify={handleVerification}
            onGenerateAiSummary={handleGenerateAiSummary}
            onUseAnalystRecommendation={handleUseAiForAnalystRecommendation}
            onUseOwnerDecision={async () => handleUseAiForDecisionForm('owner')}
            onUseExceptionDecision={async () => handleUseAiForDecisionForm('exception')}
          />
        )}

        <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
          {currentStageId === 'remediationDecision' ? (
            <AiDecisionBrief
              aiSummary={data.aiSummary}
              currentStageId={currentStageId}
              canActOnCurrentStage={data.workflowState.canActOnCurrentStage}
              generatingAiSummary={generatingAiSummary}
              onGenerateAiSummary={handleGenerateAiSummary}
              onUseAnalystRecommendation={handleUseAiForAnalystRecommendation}
              onUseOwnerDecision={async () => handleUseAiForDecisionForm('owner')}
              onUseExceptionDecision={async () => handleUseAiForDecisionForm('exception')}
            />
          ) : (
            <Card className="rounded-[1.45rem] border-border/50 bg-background/35 shadow-none">
              <CardHeader className="space-y-1.5 pb-2">
                <CardTitle className="text-xs uppercase tracking-[0.16em] text-muted-foreground">Recommendations and context</CardTitle>
                <p className="text-xs leading-relaxed text-muted-foreground">
                  Advisory guidance that helps the current actor respond in this stage.
                </p>
              </CardHeader>
              <CardContent className="space-y-4">
                {currentStageId === 'approval' ? (
                  <RecommendationSnapshotCard
                    recommendation={data.recommendations[0] ?? null}
                  />
                ) : null}
                <AiDecisionBrief
                  aiSummary={data.aiSummary}
                  currentStageId={currentStageId}
                  canActOnCurrentStage={data.workflowState.canActOnCurrentStage}
                  generatingAiSummary={generatingAiSummary}
                  onGenerateAiSummary={handleGenerateAiSummary}
                  onUseAnalystRecommendation={handleUseAiForAnalystRecommendation}
                  onUseOwnerDecision={async () => handleUseAiForDecisionForm('owner')}
                  onUseExceptionDecision={async () => handleUseAiForDecisionForm('exception')}
                />
              </CardContent>
            </Card>
          )}

          <Card className="rounded-[1.45rem] border-border/50 bg-background/35 shadow-none">
            <CardHeader className="space-y-1.5 pb-2">
              <CardTitle className="text-xs uppercase tracking-[0.16em] text-muted-foreground">Pressure and exposure</CardTitle>
              <p className="text-xs leading-relaxed text-muted-foreground">
                Operating context for this remediation, kept secondary to the current action.
              </p>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid gap-3">
                <WorkflowFact
                  label="Exposure in scope"
                  value={`${data.summary.totalVulnerabilities.toLocaleString()} vulnerabilities`}
                  detail={`${data.workflow.affectedDeviceCount.toLocaleString()} devices across ${data.workflow.affectedOwnerTeamCount.toLocaleString()} owner teams`}
                  sparkline={data.workflow.openEpisodeTrend}
                />
                <WorkflowFact
                  label="Decision posture"
                  value={data.currentDecision ? outcomeLabel(data.currentDecision.outcome) : 'No decision yet'}
                  detail={
                    data.currentDecision
                      ? approvalStatusLabel(data.currentDecision.approvalStatus)
                      : data.recommendations.length > 0
                        ? 'Security recommendation ready'
                        : 'Review exposure and capture guidance'
                  }
                />
                <WorkflowFact
                  label="Owner routing"
                  value={data.softwareOwnerTeamName ?? 'Default Team'}
                  detail={formatSoftwareOwnerRoutingDetail(
                    data.softwareOwnerTeamName,
                    data.softwareOwnerAssignmentSource,
                  )}
                />
                <WorkflowFact
                  label="Execution status"
                  value={
                    data.workflow.openPatchingTaskCount > 0
                      ? `${data.workflow.openPatchingTaskCount.toLocaleString()} open patching tasks`
                      : data.workflow.completedPatchingTaskCount > 0
                        ? `${data.workflow.completedPatchingTaskCount.toLocaleString()} completed patching tasks`
                        : 'No patching tasks yet'
                  }
                  detail={data.workflowState.currentStageDescription}
                />
              </div>
              <RemediationSummaryCards summary={data.summary} />
            </CardContent>
          </Card>
        </div>
      </div>

      <Tabs defaultValue="vulnerabilities" className="gap-4">
        <TabsList className="h-10 w-full justify-start rounded-xl bg-muted/50 p-1">
          <TabsTrigger value="vulnerabilities" className="rounded-lg px-4 text-sm">
            Vulnerabilities ({data.topVulnerabilities.length})
          </TabsTrigger>
          <TabsTrigger value="devices" className="rounded-lg px-4 text-sm">
            Devices ({data.workflow.affectedDeviceCount})
          </TabsTrigger>
          <TabsTrigger value="history" className="rounded-lg px-4 text-sm">
            History {data.currentDecision || data.recommendations.length > 0 ? `(${(data.currentDecision ? 1 : 0) + data.recommendations.length})` : ''}
          </TabsTrigger>
        </TabsList>

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
              caseId={caseId}
              queryKey={queryKey}
              onSelectVuln={setSelectedVuln}
            />
          </div>
        </TabsContent>

        <TabsContent value="devices" className="space-y-4 pt-1">
          <DevicesTab
            installations={remediationInstallations}
            teamStatuses={teamStatuses}
          />
        </TabsContent>

        <TabsContent value="history" className="pt-1">
          <DecisionHistorySection
            caseId={caseId}
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

function WorkflowFact({
  label,
  value,
  detail,
  sparkline,
}: {
  label: string
  value: string
  detail: string
  sparkline?: DecisionContext['workflow']['openEpisodeTrend']
}) {
  return (
    <div className="rounded-[1.15rem] border border-border/45 bg-background/45 px-3.5 py-3">
      <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
        {label}
      </p>
      <p className="mt-1.5 text-sm font-semibold text-foreground">
        {value}
      </p>
      <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
        {detail}
      </p>
      {sparkline ? (
        <OpenEpisodeSparkline points={sparkline} className="mt-3" />
      ) : null}
    </div>
  )
}

function toDateInputValue(value?: string | null) {
  if (!value) return ''
  return value.slice(0, 10)
}

function toIsoDateBoundary(value: string) {
  if (!value) return undefined
  return `${value}T00:00:00Z`
}

type DecisionAiSummary = DecisionContext['aiSummary']

function AiDecisionBrief({
  aiSummary,
  currentStageId,
  canActOnCurrentStage,
  generatingAiSummary,
  onGenerateAiSummary,
  onUseAnalystRecommendation,
  onUseOwnerDecision,
  onUseExceptionDecision,
}: {
  aiSummary: DecisionAiSummary
  currentStageId: RemediationStageId
  canActOnCurrentStage: boolean
  generatingAiSummary: boolean
  onGenerateAiSummary: () => Promise<void>
  onUseAnalystRecommendation: () => Promise<void>
  onUseOwnerDecision: () => Promise<void>
  onUseExceptionDecision: () => Promise<void>
}) {
  const hasGuidance = Boolean(
    aiSummary.content
      || aiSummary.ownerRecommendation
      || aiSummary.analystAssessment
      || aiSummary.exceptionRecommendation
      || aiSummary.recommendedPriority,
  )

  return (
    <Card className="rounded-[1.35rem] border-border/45 bg-background/30 shadow-none">
      <CardHeader className="space-y-1.5 pb-2">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <CardTitle className="text-xs uppercase tracking-[0.16em] text-muted-foreground">AI draft support</CardTitle>
          <AiStatusBadge aiSummary={aiSummary} />
        </div>
        <p className="text-xs leading-relaxed text-muted-foreground">
          PatchHound drafts business and triage guidance to support the workflow below. The analyst and owner still make the final decision.
        </p>
      </CardHeader>
      <CardContent className="space-y-4">
        {aiSummary.status === 'Queued' || aiSummary.status === 'Generating' ? (
          <div className="flex items-start gap-3 rounded-2xl border border-dashed border-border/70 bg-background/40 px-4 py-4">
            <LoaderCircle className="mt-0.5 size-4 animate-spin text-primary" />
            <div className="min-w-0 space-y-1">
              <p className="text-sm font-medium text-foreground">
                AI is working
              </p>
              <p className="text-sm text-muted-foreground">
                {aiSummary.status === 'Queued'
                  ? 'The guidance job has been queued and will start shortly.'
                  : 'Guidance is being generated right now.'}
              </p>
              {aiSummary.requestedAt ? (
                <p className="text-xs text-muted-foreground">
                  Requested {formatNullableDateTime(aiSummary.requestedAt)}
                </p>
              ) : null}
            </div>
          </div>
        ) : null}
        {hasGuidance ? (
          <>
            <div className="space-y-3">
              <AiAudiencePanel
                eyebrow="Business brief"
                title="What this means for the business"
                content={aiSummary.content}
                emptyState="No business summary has been generated yet."
              />

              {(aiSummary.ownerRecommendation || aiSummary.recommendedOutcome) ? (
                <AiAudiencePanel
                  eyebrow="Owner guidance"
                  title="Suggested remediation path"
                  content={aiSummary.ownerRecommendation}
                  emptyState="No owner recommendation has been generated yet."
                  callout={aiSummary.recommendedOutcome ? (
                    <div className="rounded-2xl border border-border/70 bg-background/70 px-3 py-2">
                      <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                        Suggested outcome
                      </p>
                      <p className="mt-1 text-sm font-medium text-foreground">
                        {outcomeLabel(aiSummary.recommendedOutcome)}
                      </p>
                    </div>
                  ) : null}
                  action={currentStageId === 'remediationDecision' && canActOnCurrentStage ? (
                    <Button type="button" variant="outline" size="sm" onClick={() => void onUseOwnerDecision()}>
                      Use in decision form
                    </Button>
                  ) : null}
                />
              ) : null}

              {(aiSummary.analystAssessment || aiSummary.recommendedPriority) ? (
                <AiAudiencePanel
                  eyebrow="Analyst guidance"
                  title="Suggested triage and priority"
                  content={aiSummary.analystAssessment}
                  emptyState="No analyst assessment has been generated yet."
                  callout={aiSummary.recommendedPriority ? (
                    <div className="rounded-2xl border border-border/70 bg-background/70 px-3 py-2">
                      <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                        Recommended priority
                      </p>
                      <p className="mt-1 text-sm font-medium text-foreground">
                        {aiSummary.recommendedPriority}
                      </p>
                    </div>
                  ) : null}
                  action={currentStageId === 'securityAnalysis' && canActOnCurrentStage ? (
                    <Button type="button" variant="outline" size="sm" onClick={() => void onUseAnalystRecommendation()}>
                      Use in analyst recommendation
                    </Button>
                  ) : null}
                />
              ) : null}

              {aiSummary.exceptionRecommendation ? (
                <AiAudiencePanel
                  eyebrow="Exception review"
                  title="If the team is considering deferral or acceptance"
                  content={aiSummary.exceptionRecommendation}
                  emptyState="No exception recommendation has been generated yet."
                  action={currentStageId === 'remediationDecision' && canActOnCurrentStage ? (
                    <Button type="button" variant="outline" size="sm" onClick={() => void onUseExceptionDecision()}>
                      Use in decision form
                    </Button>
                  ) : null}
                />
              ) : null}
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={onGenerateAiSummary}
                disabled={generatingAiSummary}
              >
                {generatingAiSummary ? 'Refreshing...' : hasGuidance ? 'Refresh AI guidance' : 'Ask AI'}
              </Button>
            </div>

            <AiDecisionBriefFooter aiSummary={aiSummary} />
          </>
        ) : aiSummary.canGenerate ? (
          <div className="rounded-2xl border border-dashed border-border/70 bg-background/40 px-4 py-4">
            <p className="text-sm text-muted-foreground">
              Ask AI to queue a business brief, owner guidance, analyst triage notes, and exception advice for this remediation.
            </p>
            <div className="mt-3">
              <Button
                type="button"
                onClick={onGenerateAiSummary}
                disabled={generatingAiSummary}
              >
                {generatingAiSummary ? 'Asking AI...' : 'Ask AI'}
              </Button>
            </div>
          </div>
        ) : (
          <div className="rounded-2xl border border-dashed border-border/70 bg-background/40 px-4 py-4">
            <p className="text-sm text-muted-foreground">
              {aiSummary.unavailableMessage}
            </p>
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function AiAudiencePanel({
  eyebrow,
  title,
  content,
  emptyState,
  callout,
  action,
}: {
  eyebrow: string
  title: string
  content?: string | null
  emptyState: string
  callout?: ReactNode
  action?: ReactNode
}) {
  return (
    <div className="space-y-3 rounded-[1.15rem] border border-border/60 bg-background/45 px-4 py-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
          <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
            {eyebrow}
          </p>
          <h3 className="text-sm font-medium text-foreground">
            {title}
          </h3>
        </div>
        {action}
      </div>
      {callout}
      {content ? (
        <p className="whitespace-pre-line text-sm leading-relaxed text-muted-foreground">
          {content}
        </p>
      ) : (
        <p className="text-sm text-muted-foreground">
          {emptyState}
        </p>
      )}
    </div>
  )
}

function AiDecisionBriefFooter({ aiSummary }: { aiSummary: DecisionAiSummary }) {
  return (
    <>
      <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
        <span>Generated {formatNullableDateTime(aiSummary.generatedAt)}</span>
        {aiSummary.completedAt ? (
          <span>Completed {formatNullableDateTime(aiSummary.completedAt)}</span>
        ) : null}
        {aiSummary.profileName ? (
          <span>
            via {aiSummary.profileName}
            {aiSummary.model ? ` · ${aiSummary.model}` : ''}
          </span>
        ) : null}
      </div>
      {aiSummary.isGenerating ? (
        <p className="text-xs text-muted-foreground">
          AI guidance is queued or generating in the background.
        </p>
      ) : null}
      {aiSummary.lastError ? (
        <p className="text-xs text-destructive">
          Last AI generation error: {aiSummary.lastError}
        </p>
      ) : null}
      {aiSummary.reviewStatus ? (
        <p className="text-xs text-muted-foreground">
          AI draft {aiSummary.reviewStatus.toLowerCase()}
          {aiSummary.reviewedByDisplayName ? ` by ${aiSummary.reviewedByDisplayName}` : ''}
          {aiSummary.reviewedAt ? ` on ${formatNullableDateTime(aiSummary.reviewedAt)}` : ''}
          .
        </p>
      ) : null}
    </>
  )
}

function AiStatusBadge({ aiSummary }: { aiSummary: DecisionAiSummary }) {
  const tone = {
    Ready: 'border-emerald-200/70 bg-emerald-50 text-emerald-700 dark:border-emerald-800/60 dark:bg-emerald-950/40 dark:text-emerald-300',
    Queued: 'border-sky-200/70 bg-sky-50 text-sky-700 dark:border-sky-800/60 dark:bg-sky-950/40 dark:text-sky-300',
    Generating: 'border-sky-200/70 bg-sky-50 text-sky-700 dark:border-sky-800/60 dark:bg-sky-950/40 dark:text-sky-300',
    Failed: 'border-rose-200/70 bg-rose-50 text-rose-700 dark:border-rose-800/60 dark:bg-rose-950/40 dark:text-rose-300',
    Stale: 'border-amber-200/70 bg-amber-50 text-amber-700 dark:border-amber-800/60 dark:bg-amber-950/40 dark:text-amber-300',
    Missing: 'border-border/70 bg-background/70 text-muted-foreground',
    Unavailable: 'border-border/70 bg-background/70 text-muted-foreground',
  }[aiSummary.status] ?? 'border-border/70 bg-background/70 text-muted-foreground'

  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-xs font-medium ${tone}`}>
      {aiSummary.status === 'Queued' || aiSummary.status === 'Generating' ? (
        <LoaderCircle className="size-3 animate-spin" />
      ) : null}
      {aiSummary.status}
      {aiSummary.isStale ? ' guidance' : ''}
    </span>
  )
}



function CurrentActionSection({
  data,
  caseId,
  queryKey,
  currentStageId,
  recommendationSeed,
  decisionSeed,
  approving,
  generatingAiSummary: _generatingAiSummary,
  onApproveReject,
  onVerify,
  onGenerateAiSummary: _onGenerateAiSummary,
  onUseAnalystRecommendation: _onUseAnalystRecommendation,
  onUseOwnerDecision: _onUseOwnerDecision,
  onUseExceptionDecision: _onUseExceptionDecision,
}: {
  data: DecisionContext
  caseId: string
  queryKey: readonly unknown[]
  currentStageId: RemediationStageId
  recommendationSeed: {
    token: number
    outcome?: string | null
    rationale?: string | null
    priorityOverride?: string | null
  } | null
  decisionSeed: {
    token: number
    outcome?: string | null
    justification?: string | null
    maintenanceWindowDate?: string | null
    expiryDate?: string | null
    reEvaluationDate?: string | null
  } | null
  approving: boolean
  generatingAiSummary: boolean
  onApproveReject: (action: 'approve' | 'reject' | 'cancel', justification?: string, maintenanceWindowDate?: string) => Promise<void>
  onVerify: (action: 'keepCurrentDecision' | 'chooseNewDecision') => Promise<void>
  onGenerateAiSummary: () => Promise<void>
  onUseAnalystRecommendation: () => Promise<void>
  onUseOwnerDecision: () => Promise<void>
  onUseExceptionDecision: () => Promise<void>
}) {
  const stagePresentation = {
    verification: {
      title: 'Current action: verify recurring remediation',
      description: 'Decide whether to carry the previous posture forward or send this back for a new decision.',
      eyebrow: 'Verification',
      icon: SearchCheck,
      badge: 'Review prior posture',
      shell: 'border-sky-300/30 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--color-sky-500)_12%,transparent),transparent_48%),var(--color-card)]',
      iconShell: 'border-sky-300/40 bg-sky-500/10 text-sky-700 dark:text-sky-300',
      badgeShell: 'border-sky-300/40 bg-sky-500/8 text-sky-700 dark:text-sky-300',
    },
    securityAnalysis: {
      title: 'Current action: analyst recommendation',
      description: 'Record the analyst recommendation and suggested priority. This informs the owner, but does not decide the outcome.',
      eyebrow: 'Security analysis',
      icon: ShieldAlert,
      badge: 'Advisory input only',
      shell: 'border-violet-300/30 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--color-violet-500)_12%,transparent),transparent_48%),var(--color-card)]',
      iconShell: 'border-violet-300/40 bg-violet-500/10 text-violet-700 dark:text-violet-300',
      badgeShell: 'border-violet-300/40 bg-violet-500/8 text-violet-700 dark:text-violet-300',
    },
    remediationDecision: {
      title: 'Current action: asset owner decision',
      description: 'Record the actual remediation decision. Analyst and AI guidance are advisory only.',
      eyebrow: 'Owner decision',
      icon: ClipboardCheck,
      badge: 'Owner is decision maker',
      shell: 'border-emerald-300/30 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--color-emerald-500)_12%,transparent),transparent_48%),var(--color-card)]',
      iconShell: 'border-emerald-300/40 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300',
      badgeShell: 'border-emerald-300/40 bg-emerald-500/8 text-emerald-700 dark:text-emerald-300',
    },
    approval: {
      title: 'Current action: approve or reject the owner decision',
      description: 'Approve or reject the owner’s submitted decision. Do not change the decision itself.',
      eyebrow: 'Approval',
      icon: CheckCircle,
      badge: 'Approve or reject only',
      shell: 'border-amber-300/30 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--color-amber-500)_12%,transparent),transparent_48%),var(--color-card)]',
      iconShell: 'border-amber-300/40 bg-amber-500/10 text-amber-700 dark:text-amber-300',
      badgeShell: 'border-amber-300/40 bg-amber-500/8 text-amber-700 dark:text-amber-300',
    },
    execution: {
      title: 'Current action: patching team execution',
      description: 'Track approved patch work and execution status against the maintenance window.',
      eyebrow: 'Execution',
      icon: Wrench,
      badge: 'Team follow-through',
      shell: 'border-cyan-300/30 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--color-cyan-500)_12%,transparent),transparent_48%),var(--color-card)]',
      iconShell: 'border-cyan-300/40 bg-cyan-500/10 text-cyan-700 dark:text-cyan-300',
      badgeShell: 'border-cyan-300/40 bg-cyan-500/8 text-cyan-700 dark:text-cyan-300',
    },
    closure: {
      title: 'Current action: closure review',
      description: 'Confirm whether the exposure is resolved or an active exception still applies.',
      eyebrow: 'Closure',
      icon: CheckCircle,
      badge: 'Confirm final state',
      shell: 'border-fuchsia-300/30 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--color-fuchsia-500)_12%,transparent),transparent_48%),var(--color-card)]',
      iconShell: 'border-fuchsia-300/40 bg-fuchsia-500/10 text-fuchsia-700 dark:text-fuchsia-300',
      badgeShell: 'border-fuchsia-300/40 bg-fuchsia-500/8 text-fuchsia-700 dark:text-fuchsia-300',
    },
  }[currentStageId]
  const StageIcon = stagePresentation.icon

  return (
    <Card className={`rounded-[1.8rem] ${stagePresentation.shell} ${currentStageId === 'remediationDecision' ? 'min-h-[24rem]' : ''}`}>
      <CardHeader className="space-y-2 pb-3">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="flex items-start gap-3">
            <div className={`mt-0.5 rounded-2xl border p-2.5 ${stagePresentation.iconShell}`}>
              <StageIcon className="size-5" />
            </div>
            <div className="space-y-1.5">
              <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                {stagePresentation.eyebrow}
              </p>
              <CardTitle className="text-base">{stagePresentation.title}</CardTitle>
              <p className="max-w-4xl text-sm leading-relaxed text-muted-foreground">
                {stagePresentation.description}
              </p>
            </div>
          </div>
          <span className={`inline-flex items-center rounded-full border px-2.5 py-1 text-xs font-medium ${stagePresentation.badgeShell}`}>
            {stagePresentation.badge}
          </span>
        </div>
      </CardHeader>
      <CardContent className={currentStageId === 'remediationDecision' ? 'flex flex-col justify-between pb-5' : undefined}>
        {currentStageId === 'verification' ? (
          <StageVerificationPanel
            data={data}
            canActOnCurrentStage={data.workflowState.canActOnCurrentStage}
            approving={approving}
            onVerify={onVerify}
          />
        ) : null}
        {currentStageId === 'securityAnalysis' ? (
          <RecommendationPanel
            key={recommendationSeed?.token ?? 'recommendation-panel'}
            caseId={caseId}
            recommendations={data.recommendations}
            aiAnalystAssessment={data.aiSummary.analystAssessment}
            aiRecommendedOutcome={data.aiSummary.recommendedOutcome}
            aiRecommendedPriority={data.aiSummary.recommendedPriority}
            queryKey={queryKey}
            recommendationSeed={recommendationSeed}
          />
        ) : null}
        {currentStageId === 'remediationDecision' ? (
          <div className="min-h-[15rem] space-y-5">
            {data.currentDecision?.approvalStatus === 'Rejected' && data.currentDecision ? (
              <div className="rounded-2xl border border-destructive/40 bg-destructive/6 p-4">
                <div className="flex items-start gap-3">
                  <div className="mt-0.5 rounded-full border border-destructive/30 bg-destructive/10 p-2 text-destructive">
                    <XCircle className="size-4" />
                  </div>
                  <div className="min-w-0">
                    <p className="text-sm font-semibold text-destructive">
                      Approval was rejected
                    </p>
                    <p className="mt-1 text-sm leading-relaxed text-foreground/90">
                      {data.currentDecision.latestRejection?.comment?.trim()
                        ? data.currentDecision.latestRejection.comment
                        : 'The approver rejected this remediation decision without leaving a written comment.'}
                    </p>
                    {data.currentDecision.latestRejection?.rejectedAt ? (
                      <p className="mt-2 text-xs text-muted-foreground">
                        Rejected {formatDateTime(data.currentDecision.latestRejection.rejectedAt)}
                      </p>
                    ) : null}
                  </div>
                </div>
              </div>
            ) : null}

            {data.currentDecision ? (
              <div className="rounded-2xl border border-border/70 bg-background/50 p-4">
                <div className="flex flex-wrap items-center gap-2">
                  <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-medium ${toneBadge(outcomeTone(data.currentDecision.outcome))}`}>
                    {outcomeLabel(data.currentDecision.outcome)}
                  </span>
                  <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-medium ${toneBadge(approvalStatusTone(data.currentDecision.approvalStatus))}`}>
                    {approvalStatusLabel(data.currentDecision.approvalStatus)}
                  </span>
                </div>
                <p className="mt-3 text-sm leading-relaxed text-foreground/90">
                  {data.currentDecision.justification || 'No justification was provided for this decision.'}
                </p>
              </div>
            ) : null}

            <DecisionForm
              caseId={caseId}
              queryKey={queryKey}
              readOnly={!data.workflowState.canActOnCurrentStage || currentStageId !== 'remediationDecision'}
              initialOutcome={data.currentDecision?.outcome}
              initialJustification={data.currentDecision?.justification}
              initialMaintenanceWindowDate={data.currentDecision?.maintenanceWindowDate}
              initialExpiryDate={data.currentDecision?.expiryDate}
              initialReEvaluationDate={data.currentDecision?.reEvaluationDate}
              submitLabel={data.currentDecision?.approvalStatus === 'Rejected' ? 'Update owner decision' : 'Submit owner decision'}
              decisionSeed={decisionSeed}
            />
          </div>
        ) : null}
        {currentStageId === 'approval' ? (
          <StageApprovalPanel
            data={data}
            canActOnCurrentStage={data.workflowState.canActOnCurrentStage}
            approving={approving}
            onApproveReject={onApproveReject}
          />
        ) : null}
        {currentStageId === 'execution' ? (
          <StageExecutionPanel
            data={data}
            canActOnCurrentStage={data.workflowState.canActOnCurrentStage}
            approving={approving}
            onApproveReject={onApproveReject}
          />
        ) : null}
        {currentStageId === 'closure' ? (
          <StageClosurePanel data={data} />
        ) : null}
      </CardContent>
    </Card>
  )
}

function StageVerificationPanel({
  data,
  canActOnCurrentStage,
  approving,
  onVerify,
}: {
  data: DecisionContext
  canActOnCurrentStage: boolean
  approving: boolean
  onVerify: (action: 'keepCurrentDecision' | 'chooseNewDecision') => Promise<void>
}) {
  const previousDecision = data.previousDecision

  return (
    <div className="grid gap-4 xl:grid-cols-[minmax(0,1.15fr)_minmax(320px,0.85fr)]">
      <div className="space-y-4">
        <div className="rounded-2xl border border-border/70 bg-background/60 p-4">
          <p className="text-sm leading-relaxed text-muted-foreground">
            This software has re-entered exposure. Decide whether the last posture still applies or whether it needs a fresh decision.
          </p>
        </div>
        <div className="rounded-2xl border border-border/70 bg-background/60 p-4">
          <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
            Previous decision
          </p>
          {previousDecision ? (
            <>
              <div className="mt-2 flex flex-wrap items-center gap-2">
                <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-medium ${toneBadge(outcomeTone(previousDecision.outcome))}`}>
                  {outcomeLabel(previousDecision.outcome)}
                </span>
                <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-medium ${toneBadge(approvalStatusTone(previousDecision.approvalStatus))}`}>
                  {approvalStatusLabel(previousDecision.approvalStatus)}
                </span>
              </div>
              <p className="mt-3 text-sm leading-relaxed text-foreground/90">
                {previousDecision.justification || 'No justification was recorded for the previous decision.'}
              </p>
              <p className="mt-3 text-xs text-muted-foreground">
                Decided {formatDateTime(previousDecision.decidedAt)}
              </p>
            </>
          ) : (
            <p className="mt-2 text-sm text-muted-foreground">
              No previous approved decision is available to carry forward.
            </p>
          )}
        </div>
      </div>
      <div className="space-y-3">
        <StageMetricCard
          label="Recurrence scope"
          value={data.summary.totalVulnerabilities.toLocaleString()}
          detail={`${data.workflow.affectedDeviceCount.toLocaleString()} affected devices in the recurring software scope`}
          sparkline={data.workflow.openEpisodeTrend}
        />
        <StageMetricCard
          label="Default path"
          value={previousDecision ? outcomeLabel(previousDecision.outcome) : 'No prior posture'}
          detail="Keeping the current decision carries the same posture into this new workflow episode"
        />
        <div className="rounded-2xl border border-border/70 bg-background/55 p-4">
          <div className="space-y-2">
            <Button
              onClick={() => onVerify('keepCurrentDecision')}
              disabled={!canActOnCurrentStage || approving || !previousDecision}
              className="w-full"
            >
              {approving ? 'Processing...' : 'Keep current decision'}
            </Button>
            <Button
              variant="outline"
              onClick={() => onVerify('chooseNewDecision')}
              disabled={!canActOnCurrentStage || approving}
              className="w-full"
            >
              Send back for a new decision
            </Button>
          </div>
        </div>
      </div>
    </div>
  )
}

function RecommendationSnapshotCard({
  recommendation,
  emphasized = false,
}: {
  recommendation: DecisionContext['recommendations'][number] | null
  emphasized?: boolean
}) {
  if (!recommendation) {
    return (
      <div className={emphasized
        ? 'rounded-[1.15rem] border border-violet-300/25 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--color-violet-500)_10%,transparent),transparent_55%),var(--color-card)] p-3.5'
        : 'rounded-[1.15rem] border border-dashed border-border/45 bg-background/35 p-3.5'}>
        <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
          Security recommendation
        </p>
        <p className="mt-2 text-sm font-medium text-foreground">
          No analyst recommendation recorded yet
        </p>
        <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
          The software owner team can still decide, but there is no security-analysis recommendation to reference yet.
        </p>
      </div>
    )
  }

  return (
    <div className={emphasized
      ? 'rounded-[1.15rem] border border-violet-300/25 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--color-violet-500)_10%,transparent),transparent_55%),var(--color-card)] p-3.5 shadow-[inset_0_1px_0_rgba(255,255,255,0.02)]'
      : 'rounded-[1.15rem] border border-border/45 bg-background/45 p-3.5'}>
      <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
        Security recommendation
      </p>
      <div className="mt-2 flex flex-wrap items-center gap-2">
        <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-medium ${toneBadge(outcomeTone(recommendation.recommendedOutcome))}`}>
          {outcomeLabel(recommendation.recommendedOutcome)}
        </span>
        {recommendation.priorityOverride ? (
          <span className="rounded-full border border-border/70 bg-background/70 px-2.5 py-0.5 text-[11px] font-medium text-muted-foreground">
            {recommendation.priorityOverride} priority
          </span>
        ) : null}
      </div>
      <p className="mt-3 text-sm leading-relaxed text-foreground/90">
        {recommendation.rationale}
      </p>
      <p className="mt-3 text-xs text-muted-foreground">
        {recommendation.analystDisplayName ? `${recommendation.analystDisplayName} · ` : ''}{formatDateTime(recommendation.createdAt)}
      </p>
    </div>
  )
}

function SecurityRecommendationSidecar({
  recommendation,
}: {
  recommendation: DecisionContext['recommendations'][number] | null
}) {
  if (!recommendation) {
    return (
      <div className="space-y-2">
        <p className="text-sm font-medium text-foreground">
          No analyst recommendation recorded yet
        </p>
        <p className="text-sm leading-relaxed text-muted-foreground">
          The asset owner can still decide, but there is no security-analysis recommendation to reference yet.
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-medium ${toneBadge(outcomeTone(recommendation.recommendedOutcome))}`}>
          {outcomeLabel(recommendation.recommendedOutcome)}
        </span>
        {recommendation.priorityOverride ? (
          <span className="rounded-full border border-border/70 bg-background/70 px-2.5 py-0.5 text-[11px] font-medium text-muted-foreground">
            {recommendation.priorityOverride} priority
          </span>
        ) : null}
      </div>
      <p className="text-sm leading-relaxed text-foreground/90">
        {recommendation.rationale}
      </p>
      <p className="text-xs text-muted-foreground">
        {recommendation.analystDisplayName ? `${recommendation.analystDisplayName} · ` : ''}{formatDateTime(recommendation.createdAt)}
      </p>
    </div>
  )
}

function StageApprovalPanel({
  data,
  canActOnCurrentStage,
  approving,
  onApproveReject,
}: {
  data: DecisionContext
  canActOnCurrentStage: boolean
  approving: boolean
  onApproveReject: (action: 'approve' | 'reject' | 'cancel', justification?: string, maintenanceWindowDate?: string) => Promise<void>
}) {
  if (!data.currentDecision) {
    return null
  }

  return (
    <DecisionSummaryPanel
      decision={data.currentDecision}
      readOnly={!canActOnCurrentStage}
      approving={approving}
      onApproveReject={onApproveReject}
      emphasizeApproval
      requireApprovalJustification
      requireMaintenanceWindowOnApproval={data.currentDecision.outcome === 'ApprovedForPatching'}
      rightColumn={
        <>
          <StageMetricCard
            label="Approval needed"
            value="Security review"
            detail="Review the owner decision before it becomes active"
          />
          {data.currentDecision.expiryDate ? (
            <StageMetricCard
              label="Decision timing"
              value={formatNullableDateTime(data.currentDecision.expiryDate) ?? 'Open-ended'}
              detail="If approval is delayed too long, the decision will expire"
            />
          ) : null}
        </>
      }
    />
  )
}

function StageExecutionPanel({
  data,
  canActOnCurrentStage,
  approving,
  onApproveReject,
}: {
  data: DecisionContext
  canActOnCurrentStage: boolean
  approving: boolean
  onApproveReject: (action: 'approve' | 'reject' | 'cancel', justification?: string, maintenanceWindowDate?: string) => Promise<void>
}) {
  const auditQuery = useQuery({
    queryKey: ['decision-audit-trail', data.remediationCaseId, 'execution-stage'],
    queryFn: () => fetchDecisionAuditTrail({ data: { caseId: data.remediationCaseId } }),
    enabled: Boolean(data.currentDecision),
  })

  if (!data.currentDecision) {
    return null
  }

  const latestApproverEvent = (auditQuery.data ?? [])
    .filter((entry) => entry.action === 'Approved' || entry.action === 'Denied')
    .sort((left, right) => new Date(right.timestamp).getTime() - new Date(left.timestamp).getTime())[0] ?? null

  return (
    <DecisionSummaryPanel
      decision={data.currentDecision}
      readOnly={!canActOnCurrentStage}
      approving={approving}
      onApproveReject={onApproveReject}
      narrativeContent={(
        <DecisionNarrativeTimeline
          recommendation={data.recommendations[0] ?? null}
          decision={data.currentDecision}
          approverEvent={latestApproverEvent}
        />
      )}
      rightColumn={
        <>
          <StageMetricCard
            label="Open team tasks"
            value={data.workflow.openPatchingTaskCount.toLocaleString()}
            detail={`${data.workflow.affectedOwnerTeamCount.toLocaleString()} teams still carrying execution`}
          />
          <StageMetricCard
            label="Completed tasks"
            value={data.workflow.completedPatchingTaskCount.toLocaleString()}
            detail="Completed team tasks move this remediation toward closure"
          />
          <Link
            to="/remediation/tasks"
            search={{
              page: 1,
              pageSize: 25,
              search: '',
              vendor: '',
              criticality: '',
              assetOwner: '',
              taskId: '',
              caseId: data.remediationCaseId,
              deviceAssetId: '',
            }}
            className="block rounded-2xl border border-border/70 bg-background/55 p-4 transition hover:border-foreground/20 hover:bg-muted/20"
          >
            <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
              Open remediation workbench
            </p>
            <p className="mt-2 text-sm font-semibold text-foreground">
              Open the patching team workbench
            </p>
            <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
              Jump to the filtered backlog to see which patching teams still have open work.
            </p>
          </Link>
        </>
      }
    />
  )
}

function StageClosurePanel({ data }: { data: DecisionContext }) {
  return (
    <div className="grid gap-4 xl:grid-cols-[minmax(0,1.1fr)_minmax(320px,0.9fr)]">
      <div className="rounded-2xl border border-emerald-300/70 bg-emerald-500/6 p-5">
        <p className="text-[11px] uppercase tracking-[0.16em] text-emerald-700 dark:text-emerald-300">
          Closure status
        </p>
        <h3 className="mt-2 text-xl font-semibold text-foreground">
          This remediation is ready for closure review.
        </h3>
        <p className="mt-2 text-sm leading-relaxed text-muted-foreground">
          Confirm that remediation resolved the exposure, or that an active exception still justifies closure.
        </p>
      </div>
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-1">
        <StageMetricCard
          label="Resolved exposure"
          value={data.summary.totalVulnerabilities.toLocaleString()}
          detail="Open vulnerabilities currently remaining in scope"
        />
        <StageMetricCard
          label="Completed patching tasks"
          value={data.workflow.completedPatchingTaskCount.toLocaleString()}
          detail="Recorded team execution tasks that contributed to closure"
        />
      </div>
    </div>
  )
}

function DecisionSummaryPanel({
  decision,
  readOnly,
  approving,
  onApproveReject,
  narrativeContent,
  rightColumn,
  emphasizeApproval = false,
  requireApprovalJustification = false,
  requireMaintenanceWindowOnApproval = false,
}: {
  decision: NonNullable<DecisionContext['currentDecision']>
  readOnly: boolean
  approving: boolean
  onApproveReject: (action: 'approve' | 'reject' | 'cancel', justification?: string, maintenanceWindowDate?: string) => Promise<void>
  narrativeContent?: ReactNode
  rightColumn: ReactNode
  emphasizeApproval?: boolean
  requireApprovalJustification?: boolean
  requireMaintenanceWindowOnApproval?: boolean
}) {
  const [approvalJustification, setApprovalJustification] = useState('')
  const [approvalMaintenanceWindowDate, setApprovalMaintenanceWindowDate] = useState(toDateInputValue(decision.maintenanceWindowDate))
  const isRejected = decision.approvalStatus === 'Rejected'
  const needsMaintenanceWindowOnApproval = decision.approvalStatus === 'PendingApproval' && requireMaintenanceWindowOnApproval

  return (
    <div className="grid gap-4 xl:grid-cols-[minmax(0,1.15fr)_minmax(320px,0.85fr)]">
      <div className="space-y-4">
        {isRejected ? (
          <div className="rounded-2xl border border-destructive/40 bg-destructive/6 p-4">
            <div className="flex items-start gap-3">
              <div className="mt-0.5 rounded-full border border-destructive/30 bg-destructive/10 p-2 text-destructive">
                <XCircle className="size-4" />
              </div>
              <div className="min-w-0">
                <p className="text-sm font-semibold text-destructive">
                  Approval was rejected
                </p>
                <p className="mt-1 text-sm leading-relaxed text-foreground/90">
                  {decision.latestRejection?.comment?.trim()
                    ? decision.latestRejection.comment
                    : 'The approver rejected this remediation decision without leaving a written comment.'}
                </p>
                {decision.latestRejection?.rejectedAt ? (
                  <p className="mt-2 text-xs text-muted-foreground">
                    Rejected {formatDateTime(decision.latestRejection.rejectedAt)}
                  </p>
                ) : null}
              </div>
            </div>
          </div>
        ) : null}

        <div className={`rounded-2xl border p-4 ${
          isRejected
            ? 'border-destructive/40 bg-destructive/4'
            : emphasizeApproval
              ? 'border-amber-300/70 bg-amber-500/6'
              : 'border-border/70 bg-background/60'
        }`}>
          <div className="flex flex-wrap items-center gap-2">
            <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-medium ${toneBadge(outcomeTone(decision.outcome))}`}>
              {outcomeLabel(decision.outcome)}
            </span>
            <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-medium ${toneBadge(approvalStatusTone(decision.approvalStatus))}`}>
              {approvalStatusLabel(decision.approvalStatus)}
            </span>
          </div>
          <div className="mt-3">
            {narrativeContent ?? (
              <p className="text-sm leading-relaxed text-foreground/90">
                {decision.justification || 'No justification was provided for this decision.'}
              </p>
            )}
          </div>
        </div>

        <div className="grid grid-cols-2 gap-3 text-xs text-muted-foreground sm:grid-cols-4">
          <div className="rounded-xl border border-border/60 bg-background/50 px-3 py-3">
            <span className="block font-medium text-foreground">Decided</span>
            {formatDateTime(decision.decidedAt)}
          </div>
          {isRejected && decision.latestRejection?.rejectedAt ? (
            <div className="rounded-xl border border-destructive/30 bg-destructive/5 px-3 py-3">
              <span className="block font-medium text-destructive">Rejected</span>
              {formatDateTime(decision.latestRejection.rejectedAt)}
            </div>
          ) : null}
          {decision.approvedAt && !isRejected ? (
            <div className="rounded-xl border border-border/60 bg-background/50 px-3 py-3">
              <span className="block font-medium text-foreground">Approved</span>
              {formatDateTime(decision.approvedAt)}
            </div>
          ) : null}
          {decision.maintenanceWindowDate ? (
            <div className="rounded-xl border border-border/60 bg-background/50 px-3 py-3">
              <span className="block font-medium text-foreground">Maintenance window</span>
              {formatNullableDateTime(decision.maintenanceWindowDate)}
            </div>
          ) : null}
          {decision.expiryDate ? (
            <div className="rounded-xl border border-border/60 bg-background/50 px-3 py-3">
              <span className="block font-medium text-foreground">Expires</span>
              {formatNullableDateTime(decision.expiryDate)}
            </div>
          ) : null}
          {decision.reEvaluationDate ? (
            <div className="rounded-xl border border-border/60 bg-background/50 px-3 py-3">
              <span className="block font-medium text-foreground">Re-evaluate</span>
              {formatNullableDateTime(decision.reEvaluationDate)}
            </div>
          ) : null}
        </div>

        {decision.approvalStatus === 'PendingApproval' && requireApprovalJustification ? (
          <div className="space-y-2 pt-1">
            <label
              className="text-xs font-medium text-muted-foreground"
              htmlFor={`approval-justification-${decision.id}`}
            >
              Approval justification
            </label>
            <Textarea
              id={`approval-justification-${decision.id}`}
              value={approvalJustification}
              onChange={(event) => setApprovalJustification(event.target.value)}
              placeholder="Explain why you are approving or rejecting this decision..."
              disabled={approving || readOnly}
              className="min-h-24 bg-background/70"
            />
            <p className="text-xs text-muted-foreground">
              This note is saved with the approval action.
            </p>
          </div>
        ) : null}

        {needsMaintenanceWindowOnApproval ? (
          <div className="space-y-2 pt-1">
            <label
              className="text-xs font-medium text-muted-foreground"
              htmlFor={`approval-maintenance-window-${decision.id}`}
            >
              Maintenance window date
            </label>
            <Input
              id={`approval-maintenance-window-${decision.id}`}
              type="date"
              value={approvalMaintenanceWindowDate}
              onChange={(event: React.ChangeEvent<HTMLInputElement>) => setApprovalMaintenanceWindowDate(event.target.value)}
              placeholder="Select maintenance window"
              disabled={approving || readOnly}
              className="bg-background/70"
            />
            <p className="text-xs text-muted-foreground">
              Set when the approved patch is expected to be in place.
            </p>
          </div>
        ) : null}

        <div className="flex flex-wrap gap-2 pt-1">
          {decision.approvalStatus === 'PendingApproval' ? (
            <>
              <Button
                size="sm"
                onClick={() => onApproveReject(
                  'approve',
                  approvalJustification,
                  needsMaintenanceWindowOnApproval ? toIsoDateBoundary(approvalMaintenanceWindowDate) : undefined,
                )}
                disabled={approving || readOnly || (needsMaintenanceWindowOnApproval && approvalMaintenanceWindowDate === '')}
              >
                <CheckCircle className="mr-1.5 size-3.5" />
                Approve owner decision
              </Button>
              <Button
                variant="destructive"
                size="sm"
                onClick={() => onApproveReject('reject', approvalJustification)}
                disabled={approving || readOnly}
              >
                <XCircle className="mr-1.5 size-3.5" />
                Reject owner decision
              </Button>
            </>
          ) : null}
          {!isRejected ? (
            <Button
              variant="outline"
              size="sm"
              onClick={() => onApproveReject('cancel')}
              disabled={approving || readOnly}
            >
              <Ban className="mr-1.5 size-3.5" />
              Withdraw decision
            </Button>
          ) : null}
        </div>

        {decision.overrides.length > 0 ? (
          <div className="pt-2">
            <p className="mb-1.5 text-xs font-medium text-muted-foreground">
              Vulnerability overrides ({decision.overrides.length})
            </p>
            <div className="space-y-1">
              {decision.overrides.map((ov) => (
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

      <div className="space-y-3">
        {rightColumn}
      </div>
    </div>
  )
}

function DecisionNarrativeTimeline({
  recommendation,
  decision,
  approverEvent,
}: {
  recommendation: DecisionContext['recommendations'][number] | null
  decision: NonNullable<DecisionContext['currentDecision']>
  approverEvent: { action: string; userDisplayName: string | null; justification: string | null; timestamp: string } | null
}) {
  const items: Array<{
    label: string
    actor: string
    timestamp: string | null
    tone: Tone
    badge: string | null
    text: string
  }> = [
    {
      label: 'Security recommendation',
      actor: recommendation?.analystDisplayName ?? 'Analyst',
      timestamp: recommendation?.createdAt ?? null,
      tone: recommendation ? outcomeTone(recommendation.recommendedOutcome) : 'neutral',
      badge: recommendation ? outcomeLabel(recommendation.recommendedOutcome) : null,
      text: recommendation?.rationale?.trim() || 'No analyst recommendation was recorded for this remediation.',
    },
    {
      label: 'Asset owner',
      actor: 'Decision maker',
      timestamp: decision.decidedAt,
      tone: outcomeTone(decision.outcome),
      badge: outcomeLabel(decision.outcome),
      text: decision.justification?.trim() || 'No written rationale was provided by the asset owner.',
    },
    {
      label: 'Approver',
      actor: approverEvent?.userDisplayName ?? (decision.approvalStatus === 'PendingApproval' ? 'Pending review' : 'Approver'),
      timestamp: approverEvent?.timestamp ?? decision.approvedAt ?? decision.latestRejection?.rejectedAt ?? null,
      tone:
        decision.approvalStatus === 'Approved'
          ? 'success'
          : decision.approvalStatus === 'Rejected'
            ? 'danger'
            : 'warning',
      badge:
        decision.approvalStatus === 'Approved'
          ? 'Approved'
          : decision.approvalStatus === 'Rejected'
            ? 'Rejected'
            : 'Pending',
      text:
        approverEvent?.justification?.trim()
        || decision.latestRejection?.comment?.trim()
        || (
          decision.approvalStatus === 'PendingApproval'
            ? 'Approval is still pending. No approver note has been recorded yet.'
            : 'No written approval note was recorded.'
        ),
    },
  ]

  return (
    <ol className="space-y-3">
      {items.map((item, index) => (
        <li key={item.label} className="relative pl-6">
          {index < items.length - 1 ? (
            <span
              aria-hidden="true"
              className="absolute left-[0.42rem] top-6 bottom-[-1rem] w-px bg-border/70"
            />
          ) : null}
          <span
            aria-hidden="true"
            className={`absolute left-0 top-1.5 inline-flex size-3 rounded-full border ${toneBadge(item.tone)}`}
          />
          <div className="space-y-1.5">
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-sm font-semibold text-foreground">{item.label}</span>
              {item.badge ? (
                <span className={`inline-flex rounded-full border px-2 py-0.5 text-[10px] font-medium ${toneBadge(item.tone)}`}>
                  {item.badge}
                </span>
              ) : null}
              <span className="text-xs text-muted-foreground">
                {item.actor}
                {item.timestamp ? ` · ${formatDateTime(item.timestamp)}` : ''}
              </span>
            </div>
            <p className="text-sm leading-relaxed text-foreground/90">{item.text}</p>
          </div>
        </li>
      ))}
    </ol>
  )
}

function StageMetricCard({
  label,
  value,
  detail,
  sparkline,
}: {
  label: string
  value: string
  detail: string
  sparkline?: DecisionContext['workflow']['openEpisodeTrend']
}) {
  return (
    <div className="rounded-2xl border border-border/70 bg-background/55 p-4">
      <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
        {label}
      </p>
      <p className="mt-2 text-2xl font-semibold tracking-[-0.03em] text-foreground">
        {value}
      </p>
      <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
        {detail}
      </p>
      {sparkline ? (
        <OpenEpisodeSparkline points={sparkline} className="mt-3" />
      ) : null}
    </div>
  )
}

function DevicesTab({
  installations,
  teamStatuses,
}: {
  installations?: PagedTenantSoftwareInstallations
  teamStatuses: RemediationTaskTeamStatus[]
}) {
  const items = installations?.items ?? []
  const teamStatusById = new Map(teamStatuses.map((status) => [status.ownerTeamId, status]))

  return (
    <Card className="rounded-[1.6rem] border-border/70">
      <CardHeader className="pb-2">
        <CardTitle className="text-sm">Affected devices</CardTitle>
      </CardHeader>
      <CardContent>
        {items.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-border/70 bg-background/40 px-4 py-8 text-center text-sm text-muted-foreground">
            No affected devices found.
          </div>
        ) : (
          <div className="overflow-hidden rounded-2xl border border-border/70">
            <table className="min-w-full divide-y divide-border/70 text-sm">
              <thead className="bg-muted/30 text-left text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                <tr>
                  <th className="px-4 py-3 font-medium">Device</th>
                  <th className="px-4 py-3 font-medium">Risk</th>
                  <th className="px-4 py-3 font-medium">Criticality</th>
                  <th className="px-4 py-3 font-medium">Open vulns</th>
                  <th className="px-4 py-3 font-medium">Owner</th>
                  <th className="px-4 py-3 font-medium">Task status</th>
                  <th className="px-4 py-3 font-medium">Version</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border/60 bg-card">
                {items.map((item) => {
                  const teamStatus = item.ownerTeamId ? teamStatusById.get(item.ownerTeamId) : undefined
                  return (
                    <tr key={`${item.deviceAssetId}-${item.softwareAssetId}`} className="hover:bg-muted/20">
                      <td className="px-4 py-3">
                        <Link
                          to="/devices/$id"
                          params={{ id: item.deviceAssetId }}
                          className="font-medium text-foreground hover:underline"
                        >
                          {item.deviceName}
                        </Link>
                      </td>
                      <td className="px-4 py-3 font-semibold tabular-nums text-foreground">
                        {item.currentRiskScore != null ? Math.round(item.currentRiskScore) : '—'}
                      </td>
                      <td className="px-4 py-3">
                        <span className="inline-flex rounded-full border border-border/70 bg-background px-2 py-0.5 text-xs font-medium">
                          {item.deviceCriticality}
                        </span>
                      </td>
                      <td className="px-4 py-3 tabular-nums text-foreground">
                        {item.openVulnerabilityCount.toLocaleString()}
                      </td>
                      <td className="px-4 py-3 text-muted-foreground">
                        {item.ownerTeamName ?? item.ownerUserName ?? <span className="italic">Unassigned</span>}
                      </td>
                      <td className="px-4 py-3">
                        {teamStatus ? (
                          <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-medium ${toneBadge(taskStatusTone(teamStatus.status))}`}>
                            {taskStatusLabel(teamStatus.status)}
                          </span>
                        ) : (
                          <span className="text-xs text-muted-foreground">—</span>
                        )}
                      </td>
                      <td className="px-4 py-3 font-mono text-xs text-muted-foreground">
                        {item.version ?? '—'}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </CardContent>
    </Card>
  )
}


function DecisionHistorySection({
  caseId,
  recommendations,
}: {
  caseId: string
  recommendations: DecisionContext['recommendations']
}) {
  const auditQuery = useQuery({
    queryKey: ['decision-audit-trail', caseId],
    queryFn: () => fetchDecisionAuditTrail({ data: { caseId } }),
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

function normalizeVersion(version: string | null) {
  return version?.trim() ?? ''
}

function taskStatusTone(status: string) {
  switch (status) {
    case 'Completed':
      return 'success'
    case 'InProgress':
      return 'info'
    case 'Pending':
      return 'warning'
    default:
      return 'neutral'
  }
}

function taskStatusLabel(status: string) {
  switch (status) {
    case 'InProgress':
      return 'In progress'
    default:
      return startCase(status)
  }
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
