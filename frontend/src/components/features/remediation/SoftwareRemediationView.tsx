import { useEffect, useState, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { ArrowLeft, Ban, CheckCircle, XCircle } from 'lucide-react'
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
import { approveOrRejectDecision, verifyRecurringRemediation } from '@/api/remediation.functions'
import { fetchDecisionAuditTrail } from '@/api/approval-tasks.functions'
import {
  AuditTimeline,
  type AuditTimelineEvent,
} from '@/components/features/audit/AuditTimeline'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { getApiErrorMessage } from '@/lib/api-errors'
import { toneBadge } from '@/lib/tone-classes'
import { formatDate, formatDateTime, formatNullableDateTime, startCase } from '@/lib/formatting'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { softwareQueryKeys } from '@/features/software/list-state'
import { RemediationSummaryCards } from './RemediationSummaryCards'
import { RemediationVulnTable } from './RemediationVulnTable'
import { RemediationVulnDrawer } from './RemediationVulnDrawer'
import { DecisionForm } from './DecisionForm'
import { RecommendationPanel } from './RecommendationPanel'
import { SlaIndicator } from './SlaIndicator'
import {
  RemediationStageRail,
  type RemediationStage,
  type RemediationStageId,
} from './RemediationStageRail'
import { VersionCohortChooser } from '@/components/features/software/VersionCohortChooser'
import { OpenEpisodeSparkline } from './OpenEpisodeSparkline'
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
  initialSoftwareDetail?: TenantSoftwareDetail
  initialInstallations?: PagedTenantSoftwareInstallations
  initialDeviceVersion?: string
}

export function SoftwareRemediationView({
  data,
  tenantSoftwareId,
  embedded = false,
  initialSoftwareDetail,
  initialInstallations,
  initialDeviceVersion,
}: SoftwareRemediationViewProps) {
  const [selectedVuln, setSelectedVuln] = useState<DecisionVuln | null>(null)
  const { selectedTenantId } = useTenantScope()
  const queryClient = useQueryClient()
  const queryKey = softwareQueryKeys.remediation(selectedTenantId, tenantSoftwareId)
  const [deviceVersion, setDeviceVersion] = useState(initialDeviceVersion ?? '')

  const [approving, setApproving] = useState(false)
  const [stageError, setStageError] = useState<string | null>(null)
  const workflowId = data.workflowState.workflowId
  const currentStageId = data.workflowState.currentStage as RemediationStageId
  const stages: RemediationStage[] = data.workflowState.stages.map((stage) => ({
    id: stage.id as RemediationStageId,
    label: stage.label,
    state: stage.state as RemediationStage['state'],
    description: stage.description,
  }))

  const softwareDetailQuery = useQuery({
    queryKey: softwareQueryKeys.detail(selectedTenantId, tenantSoftwareId),
    queryFn: () => fetchTenantSoftwareDetail({ data: { id: tenantSoftwareId } }),
    initialData: initialSoftwareDetail,
  })

  const softwareDetail = softwareDetailQuery.data ?? initialSoftwareDetail
  const normalizedDeviceVersion = deviceVersion || normalizeVersion(softwareDetail?.versionCohorts[0]?.version ?? null)

  useEffect(() => {
    if (!softwareDetail) {
      return
    }

    const defaultVersion = normalizeVersion(softwareDetail.versionCohorts[0]?.version ?? null)
    if (!deviceVersion && defaultVersion) {
      setDeviceVersion(defaultVersion)
    }
  }, [softwareDetail, deviceVersion])

  const installationsQuery = useQuery({
    queryKey: softwareQueryKeys.installations(selectedTenantId, tenantSoftwareId, normalizedDeviceVersion, 1, 25),
    queryFn: () => fetchTenantSoftwareInstallations({
      data: {
        id: tenantSoftwareId,
        version: normalizedDeviceVersion || undefined,
        activeOnly: true,
        page: 1,
        pageSize: 25,
      },
    }),
    enabled: Boolean(softwareDetail),
    initialData:
      initialInstallations && (initialDeviceVersion ?? '') === normalizedDeviceVersion
        ? initialInstallations
        : undefined,
  })

  const remediationInstallations = installationsQuery.data ?? initialInstallations
  const teamStatusesQuery = useQuery({
    queryKey: ['remediation-team-statuses', selectedTenantId, tenantSoftwareId],
    queryFn: () => fetchRemediationTaskTeamStatuses({ data: { tenantSoftwareId } }),
  })
  const teamStatuses = teamStatusesQuery.data ?? []

  async function handleApproveReject(action: 'approve' | 'reject' | 'cancel', justification?: string) {
    if (!data.currentDecision) return
    setApproving(true)
    setStageError(null)
    try {
      await approveOrRejectDecision({
        data: {
          tenantSoftwareId,
          workflowId,
          decisionId: data.currentDecision.id,
          action,
          justification,
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
    if (!workflowId) return
    setApproving(true)
    setStageError(null)
    try {
      await verifyRecurringRemediation({
        data: {
          workflowId,
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

      <Card className="rounded-[1.6rem] border-border/70">
        <CardContent className="grid gap-3 pt-6 md:grid-cols-3">
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
                  ? `${data.recommendations.length.toLocaleString()} analyst recommendations ready`
                  : 'Review exposure and capture guidance'
            }
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
        </CardContent>
      </Card>

      <StagePanel
        data={data}
        tenantSoftwareId={tenantSoftwareId}
        queryKey={queryKey}
        currentStageId={currentStageId}
        currentActorSummary={data.workflowState.currentActorSummary}
        canActOnCurrentStage={data.workflowState.canActOnCurrentStage}
        workflowId={workflowId}
        approving={approving}
        stageError={stageError}
        onApproveReject={handleApproveReject}
        onVerify={handleVerification}
      />

      <RemediationSummaryCards summary={data.summary} />

      <Tabs defaultValue="decision" className="gap-4">
        <TabsList className="h-10 w-full justify-start rounded-xl bg-muted/50 p-1">
          <TabsTrigger value="decision" className="rounded-lg px-4 text-sm">
            Decision
          </TabsTrigger>
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
                      workflowId={workflowId}
                      recommendations={data.recommendations}
                      queryKey={queryKey}
                    />
                  </div>
                </div>
              ) : (
                <RecommendationPanel
                  tenantSoftwareId={tenantSoftwareId}
                  workflowId={workflowId}
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

        <TabsContent value="devices" className="space-y-4 pt-1">
          <DevicesTab
            detail={softwareDetail}
            installations={remediationInstallations}
            selectedVersion={normalizedDeviceVersion}
            onSelectVersion={setDeviceVersion}
            teamStatuses={teamStatuses}
          />
        </TabsContent>

        <TabsContent value="history" className="pt-1">
          <DecisionHistorySection
            tenantSoftwareId={tenantSoftwareId}
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
    <div className="rounded-2xl border border-border/60 bg-background/55 px-4 py-3">
      <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
        {label}
      </p>
      <p className="mt-2 text-base font-semibold text-foreground">
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

function StagePanel({
  data,
  tenantSoftwareId,
  queryKey,
  currentStageId,
  currentActorSummary,
  canActOnCurrentStage,
  workflowId,
  approving,
  stageError,
  onApproveReject,
  onVerify,
}: {
  data: DecisionContext
  tenantSoftwareId: string
  queryKey: readonly unknown[]
  currentStageId: RemediationStageId
  currentActorSummary: string
  canActOnCurrentStage: boolean
  workflowId: string | null
  approving: boolean
  stageError: string | null
  onApproveReject: (action: 'approve' | 'reject' | 'cancel', justification?: string) => Promise<void>
  onVerify: (action: 'keepCurrentDecision' | 'chooseNewDecision') => Promise<void>
}) {
  return (
    <Card className="rounded-[1.8rem] border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_8%,transparent),transparent_58%),var(--color-card)]">
      <CardContent className="space-y-4 pt-6">
        <div className={`rounded-2xl border px-4 py-3 text-sm ${
          canActOnCurrentStage
            ? 'border-emerald-300/70 bg-emerald-500/6 text-foreground'
            : 'border-amber-300/70 bg-amber-500/6 text-foreground'
        }`}>
          <p className="font-medium">
            {canActOnCurrentStage ? 'You can complete this stage.' : 'You can review this stage, but you cannot complete it.'}
          </p>
          <p className="mt-1 text-muted-foreground">
            {currentActorSummary}
          </p>
        </div>
        {stageError ? (
          <div className="rounded-2xl border border-destructive/40 bg-destructive/5 px-4 py-3 text-sm text-destructive">
            {stageError}
          </div>
        ) : null}
        {currentStageId === 'verification' ? (
          <StageVerificationPanel
            data={data}
            canActOnCurrentStage={canActOnCurrentStage}
            approving={approving}
            onVerify={onVerify}
          />
        ) : null}
        {currentStageId === 'securityAnalysis' ? (
          <StageSecurityAnalysisPanel
            data={data}
            tenantSoftwareId={tenantSoftwareId}
            workflowId={workflowId}
            queryKey={queryKey}
            canActOnCurrentStage={canActOnCurrentStage}
          />
        ) : null}
        {currentStageId === 'remediationDecision' ? (
          <StageRemediationDecisionPanel
            data={data}
            tenantSoftwareId={tenantSoftwareId}
            workflowId={workflowId}
            queryKey={queryKey}
            canActOnCurrentStage={canActOnCurrentStage}
            approving={approving}
            onApproveReject={onApproveReject}
          />
        ) : null}
        {currentStageId === 'approval' ? (
          <StageApprovalPanel
            data={data}
            canActOnCurrentStage={canActOnCurrentStage}
            approving={approving}
            onApproveReject={onApproveReject}
          />
        ) : null}
        {currentStageId === 'execution' ? (
          <StageExecutionPanel
            data={data}
            canActOnCurrentStage={canActOnCurrentStage}
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
            This software has re-entered exposure after a previous remediation was closed. Verify whether the last decision should stay in place, or route the software back for a new decision.
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
          detail="Keeping the current decision carries the same posture forward into the new workflow episode"
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
              Choose a new decision
            </Button>
          </div>
          {!canActOnCurrentStage ? (
            <p className="mt-3 text-xs text-muted-foreground">
              You can review the previous decision, but only the current stage owner can confirm or replace it.
            </p>
          ) : null}
        </div>
      </div>
    </div>
  )
}

function StageSecurityAnalysisPanel({
  data,
  tenantSoftwareId,
  workflowId,
  queryKey,
  canActOnCurrentStage,
}: {
  data: DecisionContext
  tenantSoftwareId: string
  workflowId: string | null
  queryKey: readonly unknown[]
  canActOnCurrentStage: boolean
}) {
  return (
    <div className="grid gap-4 xl:grid-cols-[minmax(0,1.1fr)_minmax(320px,0.9fr)]">
      <div className="space-y-4">
        <div className="rounded-2xl border border-border/70 bg-background/60 p-4">
          <p className="text-sm leading-relaxed text-muted-foreground">
            Security analysis uses the shared exposure view to recommend the next move. This scope currently carries{' '}
            {data.summary.totalVulnerabilities.toLocaleString()} open vulnerabilities across{' '}
            {data.workflow.affectedDeviceCount.toLocaleString()} affected devices.
          </p>
        </div>
        <RecommendationPanel
          tenantSoftwareId={tenantSoftwareId}
          workflowId={workflowId}
          recommendations={data.recommendations}
          queryKey={queryKey}
          readOnly={!canActOnCurrentStage}
        />
      </div>
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-1">
        <StageMetricCard
          label="Open vulnerabilities"
          value={data.summary.totalVulnerabilities.toLocaleString()}
          detail={`${(data.summary.criticalCount + data.summary.highCount).toLocaleString()} critical or high`}
        />
        <StageMetricCard
          label="Threat pressure"
          value={data.summary.withKnownExploit.toLocaleString()}
          detail={`${data.summary.withActiveAlert.toLocaleString()} active alerts`}
        />
        <StageMetricCard
          label="Owner-team scope"
          value={data.workflow.affectedOwnerTeamCount.toLocaleString()}
          detail={`${data.workflow.affectedDeviceCount.toLocaleString()} devices in scope`}
          sparkline={data.workflow.openEpisodeTrend}
        />
      </div>
    </div>
  )
}

function StageRemediationDecisionPanel({
  data,
  tenantSoftwareId,
  workflowId,
  queryKey,
  canActOnCurrentStage,
  approving,
  onApproveReject,
}: {
  data: DecisionContext
  tenantSoftwareId: string
  workflowId: string | null
  queryKey: readonly unknown[]
  canActOnCurrentStage: boolean
  approving: boolean
  onApproveReject: (action: 'approve' | 'reject' | 'cancel', justification?: string) => Promise<void>
}) {
  const latestRecommendation = data.recommendations[0] ?? null

  if (!data.currentDecision) {
    return (
      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.1fr)_minmax(320px,0.9fr)]">
        <div className="space-y-4">
          <div className="rounded-2xl border border-border/70 bg-background/60 p-4">
            <p className="text-sm leading-relaxed text-muted-foreground">
              No remediation decision has been recorded yet. The software owner team now chooses how the organization should handle this software-wide exposure.
            </p>
          </div>
          <DecisionForm
            tenantSoftwareId={tenantSoftwareId}
            workflowId={workflowId}
            queryKey={queryKey}
            readOnly={!canActOnCurrentStage}
          />
        </div>
        <div className="space-y-3">
          <RecommendationSnapshotCard
            recommendation={latestRecommendation}
            recommendationCount={data.recommendations.length}
          />
          <StageMetricCard
            label="Analyst guidance"
            value={data.recommendations.length.toLocaleString()}
            detail="Recommendations available to inform the decision"
          />
          <StageMetricCard
            label="Current pressure"
            value={data.summary.totalVulnerabilities.toLocaleString()}
            detail={`${(data.summary.criticalCount + data.summary.highCount).toLocaleString()} critical or high vulnerabilities`}
          />
        </div>
      </div>
    )
  }

  return (
    <DecisionSummaryPanel
      decision={data.currentDecision}
      readOnly={!canActOnCurrentStage}
      approving={approving}
      onApproveReject={onApproveReject}
      rightColumn={
        <>
          <RecommendationSnapshotCard
            recommendation={latestRecommendation}
            recommendationCount={data.recommendations.length}
          />
          <StageMetricCard
            label="Decision posture"
            value={outcomeLabel(data.currentDecision.outcome)}
            detail={approvalStatusLabel(data.currentDecision.approvalStatus)}
          />
          <StageMetricCard
            label="Execution scope"
            value={data.workflow.affectedOwnerTeamCount.toLocaleString()}
            detail={`${data.workflow.affectedDeviceCount.toLocaleString()} devices could be affected`}
            sparkline={data.workflow.openEpisodeTrend}
          />
        </>
      }
    />
  )
}

function RecommendationSnapshotCard({
  recommendation,
  recommendationCount,
}: {
  recommendation: DecisionContext['recommendations'][number] | null
  recommendationCount: number
}) {
  if (!recommendation) {
    return (
      <div className="rounded-2xl border border-dashed border-border/70 bg-background/45 p-4">
        <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
          Security recommendation
        </p>
        <p className="mt-2 text-sm font-medium text-foreground">
          No recommendation recorded yet
        </p>
        <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
          The software owner team can still decide, but there is no security-analysis recommendation to reference yet.
        </p>
      </div>
    )
  }

  return (
    <div className="rounded-2xl border border-border/70 bg-background/60 p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
          Security recommendation
        </p>
        {recommendationCount > 1 ? (
          <span className="text-[11px] font-medium text-muted-foreground">
            Latest of {recommendationCount}
          </span>
        ) : null}
      </div>
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

function StageApprovalPanel({
  data,
  canActOnCurrentStage,
  approving,
  onApproveReject,
}: {
  data: DecisionContext
  canActOnCurrentStage: boolean
  approving: boolean
  onApproveReject: (action: 'approve' | 'reject' | 'cancel', justification?: string) => Promise<void>
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
      rightColumn={
        <>
          <StageMetricCard
            label="Approval needed"
            value="Security review"
            detail="Risk acceptance and alternate mitigation require approval before they become active"
          />
          {data.currentDecision.expiryDate ? (
            <StageMetricCard
              label="Approval window"
              value={formatNullableDateTime(data.currentDecision.expiryDate) ?? 'Open-ended'}
              detail="If approval is not completed in time, the decision will expire"
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
  onApproveReject: (action: 'approve' | 'reject' | 'cancel', justification?: string) => Promise<void>
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
      rightColumn={
        <>
          <StageMetricCard
            label="Open patching tasks"
            value={data.workflow.openPatchingTaskCount.toLocaleString()}
            detail={`${data.workflow.affectedOwnerTeamCount.toLocaleString()} owner teams carrying execution`}
          />
          <StageMetricCard
            label="Completed tasks"
            value={data.workflow.completedPatchingTaskCount.toLocaleString()}
            detail="Completed team tasks feed toward remediation closure"
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
              tenantSoftwareId: data.tenantSoftwareId,
              deviceAssetId: '',
            }}
            className="block rounded-2xl border border-border/70 bg-background/55 p-4 transition hover:border-foreground/20 hover:bg-muted/20"
          >
            <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
              Open remediation workbench
            </p>
            <p className="mt-2 text-sm font-semibold text-foreground">
              Review live patching tasks for this software
            </p>
            <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
              Jump to the filtered backlog to see which owner teams still have open execution work.
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
          Remediation complete
        </p>
        <h3 className="mt-2 text-xl font-semibold text-foreground">
          The approved patching flow has cleared this software exposure.
        </h3>
        <p className="mt-2 text-sm leading-relaxed text-muted-foreground">
          All linked vulnerabilities in the active software scope have been resolved, so the remediation can stay closed unless new exposure appears later.
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
  rightColumn,
  emphasizeApproval = false,
  requireApprovalJustification = false,
}: {
  decision: NonNullable<DecisionContext['currentDecision']>
  readOnly: boolean
  approving: boolean
  onApproveReject: (action: 'approve' | 'reject' | 'cancel', justification?: string) => Promise<void>
  rightColumn: ReactNode
  emphasizeApproval?: boolean
  requireApprovalJustification?: boolean
}) {
  const [approvalJustification, setApprovalJustification] = useState('')
  const isRejected = decision.approvalStatus === 'Rejected'

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
          <p className="mt-3 text-sm leading-relaxed text-foreground/90">
            {decision.justification || 'No justification was provided for this decision.'}
          </p>
        </div>

        <div className="grid grid-cols-2 gap-3 text-xs text-muted-foreground sm:grid-cols-4">
          <div className="rounded-xl border border-border/60 bg-background/50 px-3 py-3">
            <span className="block font-medium text-foreground">Decided</span>
            {formatDateTime(decision.decidedAt)}
          </div>
          {decision.approvedAt ? (
            <div className="rounded-xl border border-border/60 bg-background/50 px-3 py-3">
              <span className="block font-medium text-foreground">Approved</span>
              {formatDateTime(decision.approvedAt)}
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
              placeholder="Explain why you are approving or rejecting this remediation decision..."
              disabled={approving || readOnly}
              className="min-h-24 bg-background/70"
            />
            <p className="text-xs text-muted-foreground">
              This note is submitted with the approval action.
            </p>
          </div>
        ) : null}

        <div className="flex flex-wrap gap-2 pt-1">
          {decision.approvalStatus === 'PendingApproval' ? (
            <>
              <Button
                size="sm"
                onClick={() => onApproveReject('approve', approvalJustification)}
                disabled={approving || readOnly}
              >
                <CheckCircle className="mr-1.5 size-3.5" />
                Approve
              </Button>
              <Button
                variant="destructive"
                size="sm"
                onClick={() => onApproveReject('reject', approvalJustification)}
                disabled={approving || readOnly}
              >
                <XCircle className="mr-1.5 size-3.5" />
                Reject
              </Button>
            </>
          ) : null}
          <Button
            variant="outline"
            size="sm"
            onClick={() => onApproveReject('cancel')}
            disabled={approving || readOnly}
          >
            <Ban className="mr-1.5 size-3.5" />
            Cancel Decision
          </Button>
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
  detail,
  installations,
  selectedVersion,
  onSelectVersion,
  teamStatuses,
}: {
  detail?: TenantSoftwareDetail
  installations?: PagedTenantSoftwareInstallations
  selectedVersion: string
  onSelectVersion: (version: string) => void
  teamStatuses: RemediationTaskTeamStatus[]
}) {
  const items = installations?.items ?? []
  const ownerTeamCount = new Set(items.map((item) => item.ownerTeamId).filter(Boolean)).size
  const ownerUserCount = new Set(items.map((item) => item.ownerUserId).filter(Boolean)).size
  const teamStatusById = new Map(teamStatuses.map((status) => [status.ownerTeamId, status]))

  return (
    <div className="space-y-4">
      <Card className="rounded-[1.6rem] border-border/70">
        <CardHeader className="pb-2">
          <CardTitle className="text-sm">Execution scope by version cohort</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          {detail ? (
            <VersionCohortChooser
              title="Execution scope by version cohort"
              description="Choose a cohort to see which devices and owner teams are involved in this remediation."
              cohorts={detail.versionCohorts}
              selectedVersion={selectedVersion}
              onSelectVersion={onSelectVersion}
              formatVersion={formatVersion}
              normalizeVersion={normalizeVersion}
            />
          ) : (
            <p className="text-sm text-muted-foreground">
              Loading version cohorts for this software scope.
            </p>
          )}

          <div className="grid gap-3 md:grid-cols-3">
            <StageMetricCard
              label="Visible devices"
              value={items.length.toLocaleString()}
              detail="Devices in the selected version cohort"
            />
            <StageMetricCard
              label="Assigned owner teams"
              value={ownerTeamCount.toLocaleString()}
              detail="Teams with explicit ownership in this cohort"
            />
            <StageMetricCard
              label="Named owners"
              value={ownerUserCount.toLocaleString()}
              detail="Devices carrying direct user ownership"
            />
          </div>
        </CardContent>
      </Card>

      <Card className="rounded-[1.6rem] border-border/70">
        <CardHeader className="pb-2">
          <CardTitle className="text-sm">Affected devices</CardTitle>
        </CardHeader>
        <CardContent>
          {items.length === 0 ? (
            <div className="rounded-2xl border border-dashed border-border/70 bg-background/40 px-4 py-8 text-center text-sm text-muted-foreground">
              No active devices were found for this version cohort.
            </div>
          ) : (
            <div className="overflow-hidden rounded-2xl border border-border/70">
              <table className="min-w-full divide-y divide-border/70 text-sm">
                <thead className="bg-muted/30 text-left text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                  <tr>
                    <th className="px-4 py-3 font-medium">Device</th>
                    <th className="px-4 py-3 font-medium">Owner</th>
                    <th className="px-4 py-3 font-medium">Task status</th>
                    <th className="px-4 py-3 font-medium">Criticality</th>
                    <th className="px-4 py-3 font-medium">Open vulns</th>
                    <th className="px-4 py-3 font-medium">Last seen</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/60 bg-card">
                  {items.map((item) => (
                    <tr key={`${item.deviceAssetId}-${item.softwareAssetId}`} className="align-top">
                      {(() => {
                        const teamStatus = item.ownerTeamId ? teamStatusById.get(item.ownerTeamId) : undefined
                        return (
                          <>
                      <td className="px-4 py-3">
                        <div className="font-medium text-foreground">{item.deviceName}</div>
                        <div className="mt-1 text-xs text-muted-foreground">
                          {formatVersion(item.version)} · episode {item.currentEpisodeNumber}
                        </div>
                      </td>
                      <td className="px-4 py-3">
                        <div className="text-foreground">
                          {item.ownerTeamName ?? item.ownerUserName ?? 'Unassigned'}
                        </div>
                        <div className="mt-1 text-xs text-muted-foreground">
                          {item.ownerTeamName
                            ? 'Team owner'
                            : item.ownerUserName
                              ? 'Named owner'
                              : 'No owner assigned'}
                        </div>
                      </td>
                      <td className="px-4 py-3">
                        {teamStatus ? (
                          <>
                            <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-medium ${toneBadge(taskStatusTone(teamStatus.status))}`}>
                              {taskStatusLabel(teamStatus.status)}
                            </span>
                            <div className="mt-1 text-xs text-muted-foreground">
                              Due {formatDate(teamStatus.dueDate)}
                            </div>
                          </>
                        ) : (
                          <div className="text-xs text-muted-foreground">
                            No owner-team task
                          </div>
                        )}
                      </td>
                      <td className="px-4 py-3">
                        <span className="inline-flex rounded-full border border-border/70 bg-background px-2 py-0.5 text-xs font-medium">
                          {item.deviceCriticality}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-foreground">
                        {item.openVulnerabilityCount.toLocaleString()}
                      </td>
                      <td className="px-4 py-3 text-muted-foreground">
                        {formatDateTime(item.lastSeenAt)}
                      </td>
                          </>
                        )
                      })()}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  )
}


function DecisionHistorySection({
  tenantSoftwareId,
  recommendations,
}: {
  tenantSoftwareId: string
  recommendations: DecisionContext['recommendations']
}) {
  const auditQuery = useQuery({
    queryKey: ['decision-audit-trail', tenantSoftwareId],
    queryFn: () => fetchDecisionAuditTrail({ data: { tenantSoftwareId } }),
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

function formatVersion(version: string | null) {
  return version?.trim() || 'Version unknown'
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
