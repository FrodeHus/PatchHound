import { useMemo, useState, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Bot, ExternalLink, LoaderCircle, Maximize2, NotebookPen, Pencil, Save, SearchCheck, ShieldAlert, Trash2 } from 'lucide-react'
import type { DecisionContext, DecisionVuln, ThreatIntel } from '@/api/remediation.schemas'
import { addRecommendation } from '@/api/remediation.functions'
import { requestVulnerabilityAssessment } from '@/api/vulnerabilities.functions'
import { createWorkNote, deleteWorkNote, fetchWorkNotes, updateWorkNote } from '@/api/work-notes.functions'
import type { WorkNote } from '@/api/work-notes.schemas'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { MarkdownViewer } from '@/components/ui/markdown-viewer'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Textarea } from '@/components/ui/textarea'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { getApiErrorMessage } from '@/lib/api-errors'
import { formatDateTime, formatNullableDateTime, startCase } from '@/lib/formatting'
import { toneBadge } from '@/lib/tone-classes'
import { cn } from '@/lib/utils'
import {
  formatSoftwareOwnerRoutingDetail,
  outcomeLabel,
  outcomeTone,
  severityTone,
} from './remediation-utils'
import { MetricRail, type MetricRailItem, type MetricRailTone } from './MetricRail'
import { PatchAssessmentPanel } from './PatchAssessmentPanel'

type SecurityAnalystWorkbenchProps = {
  data: DecisionContext
  caseId: string
  queryKey: readonly unknown[]
}

const OUTCOMES = [
  'ApprovedForPatching',
  'RiskAcceptance',
  'AlternateMitigation',
  'PatchingDeferred',
] as const

const LONG_THREAT_INTEL_SUMMARY_LENGTH = 700
const LONG_THREAT_INTEL_SUMMARY_LINES = 10

export function SecurityAnalystWorkbench({ data, caseId, queryKey }: SecurityAnalystWorkbenchProps) {
  const queryClient = useQueryClient()
  const currentRecommendation = data.recommendations[0] ?? null
  const [selectedVulnerability, setSelectedVulnerability] = useState<DecisionVuln | null>(null)
  const [recommendedOutcome, setRecommendedOutcome] = useState(currentRecommendation?.recommendedOutcome ?? '')
  const [rationale, setRationale] = useState(currentRecommendation?.rationale ?? '')
  const [priorityOverride, setPriorityOverride] = useState(currentRecommendation?.priorityOverride ?? '')
  const [isSaving, setIsSaving] = useState(false)
  const [requestingAssessment, setRequestingAssessment] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const vulnerabilities = data.openVulnerabilities.length > 0 ? data.openVulnerabilities : data.topVulnerabilities
  const topDrivers = useMemo(() => vulnerabilities.slice(0, 4), [vulnerabilities])
  const hiddenDriverCount = Math.max(vulnerabilities.length - topDrivers.length, 0)
  const threatDriverCount = vulnerabilities.filter((vulnerability) =>
    vulnerability.knownExploited || vulnerability.publicExploit || vulnerability.activeAlert
  ).length
  const highestRiskDriver = useMemo(
    () => vulnerabilities.reduce<DecisionVuln | null>((highest, vulnerability) => {
      const score = vulnerability.effectiveScore ?? vulnerability.vendorScore ?? 0
      const highestScore = highest?.effectiveScore ?? highest?.vendorScore ?? 0
      return score > highestScore ? vulnerability : highest
    }, null),
    [vulnerabilities],
  )
  const highestRiskDriverDetail = highestRiskDriver
    ? formatSeverityScore(
      highestRiskDriver.effectiveSeverity ?? highestRiskDriver.vendorSeverity,
      highestRiskDriver.effectiveScore ?? highestRiskDriver.vendorScore,
    )
    : 'No active exposure'
  const canSave = recommendedOutcome !== '' && rationale.trim().length > 0
  const displaySoftwareName = startCase(data.softwareName)
  const softwareIdentity = data.softwareVendor
    && !displaySoftwareName.toLowerCase().startsWith(data.softwareVendor.toLowerCase())
    ? `${data.softwareVendor} ${displaySoftwareName}`
    : displaySoftwareName
  const metricRailItems = buildMetricRailItems({
    data,
    highestRiskDriver,
    highestRiskDriverDetail,
    threatDriverCount,
  })

  async function handleSaveRecommendation() {
    if (!canSave) return
    setIsSaving(true)
    setError(null)
    try {
      await addRecommendation({
        data: {
          caseId,
          recommendedOutcome,
          rationale: rationale.trim(),
          priorityOverride: priorityOverride || undefined,
        },
      })
      await Promise.all([
        queryClient.invalidateQueries({ queryKey }),
        queryClient.invalidateQueries({ queryKey: ['my-tasks'] }),
      ])
    } catch (err) {
      setError(getApiErrorMessage(err, 'Unable to save the analyst recommendation.'))
    } finally {
      setIsSaving(false)
    }
  }

  async function handleRequestAssessment() {
    const vulnerabilityId = data.patchAssessment.vulnerabilityId ?? vulnerabilities[0]?.vulnerabilityId
    if (!vulnerabilityId) return

    setRequestingAssessment(true)
    setError(null)
    try {
      await requestVulnerabilityAssessment({ data: { vulnerabilityId } })
      await queryClient.invalidateQueries({
        predicate: ({ queryKey: candidateKey }) =>
          isDecisionContextQueryForCase(candidateKey, caseId),
      })
    } catch (err) {
      setError(getApiErrorMessage(err, 'Unable to request patch assessment.'))
    } finally {
      setRequestingAssessment(false)
    }
  }

  return (
    <section className="space-y-5">
      <header className="rounded-lg border border-border/70 bg-card px-5 py-4">
        <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
          <div className="min-w-0 space-y-2">
            <p className="text-xs uppercase tracking-[0.16em] text-muted-foreground">
              Security analyst workbench
            </p>
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-2xl font-semibold leading-tight">
                {softwareIdentity}
              </h1>
              {data.softwareCategory ? (
                <Badge variant="outline">{data.softwareCategory}</Badge>
              ) : null}
              <Badge variant="outline">
                {data.workflowState.currentStageLabel}
              </Badge>
            </div>
            <p className="max-w-4xl text-sm leading-relaxed text-muted-foreground">
              {data.softwareDescription ??
                "Review the open exposure and capture the recommendation the asset owner will use to decide the remediation path."}
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <Link
              to="/remediation/cases/$caseId"
              params={{ caseId }}
              className="inline-flex h-9 items-center gap-2 rounded-md border border-border bg-background px-3 text-sm font-medium hover:bg-muted"
            >
              Full case
              <ExternalLink className="size-3.5" />
            </Link>
          </div>
        </div>
        <MetricRail items={metricRailItems} className="mt-4" />
      </header>

      {error ? (
        <div
          role="alert"
          className="rounded-lg border border-destructive/35 bg-destructive/8 px-4 py-3 text-sm text-destructive"
        >
          {error}
        </div>
      ) : null}

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.35fr)_minmax(340px,0.75fr)]">
        <Card className="shadow-none">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <SearchCheck className="size-4 text-primary" />
              Analyst recommendation
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            {currentRecommendation ? (
              <div className="rounded-lg border border-border/70 bg-muted/25 px-3 py-2">
                <div className="flex flex-wrap items-center gap-2">
                  <span
                    className={cn(
                      "inline-flex rounded-full border px-2 py-0.5 text-xs font-medium",
                      toneBadge(
                        outcomeTone(currentRecommendation.recommendedOutcome),
                      ),
                    )}
                  >
                    {outcomeLabel(currentRecommendation.recommendedOutcome)}
                  </span>
                  {currentRecommendation.priorityOverride ? (
                    <span className="text-xs text-muted-foreground">
                      {currentRecommendation.priorityOverride} priority
                    </span>
                  ) : null}
                </div>
                <p className="mt-2 text-sm text-muted-foreground">
                  Saved{" "}
                  {currentRecommendation.analystDisplayName
                    ? `by ${currentRecommendation.analystDisplayName} `
                    : ""}
                  {formatDateTime(currentRecommendation.createdAt)}
                </p>
              </div>
            ) : null}


            <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_180px]">
              <div className="space-y-2">
                <label
                  htmlFor="recommended-outcome"
                  className="text-sm font-medium"
                >
                  Recommended remediation
                </label>
                <Select
                  value={recommendedOutcome}
                  onValueChange={(value) => setRecommendedOutcome(value ?? "")}
                >
                  <SelectTrigger id="recommended-outcome">
                    <SelectValue placeholder="Select action..." />
                  </SelectTrigger>
                  <SelectContent>
                    {OUTCOMES.map((outcome) => (
                      <SelectItem key={outcome} value={outcome}>
                        {outcomeLabel(outcome)}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2">
                <label
                  htmlFor="priority-override"
                  className="text-sm font-medium"
                >
                  Priority
                </label>
                <Select
                  value={priorityOverride || "none"}
                  onValueChange={(value) =>
                    setPriorityOverride(value === "none" ? "" : (value ?? ""))
                  }
                >
                  <SelectTrigger id="priority-override">
                    <SelectValue placeholder="No priority" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="none">No priority</SelectItem>
                    <SelectItem value="Critical">Critical</SelectItem>
                    <SelectItem value="High">High</SelectItem>
                    <SelectItem value="Medium">Medium</SelectItem>
                    <SelectItem value="Low">Low</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>

            <div className="space-y-2">
              <label
                htmlFor="analyst-recommendation-rationale"
                className="text-sm font-medium"
              >
                Recommendation rationale
              </label>
              <Textarea
                id="analyst-recommendation-rationale"
                value={rationale}
                onChange={(event) => setRationale(event.target.value)}
                rows={7}
                placeholder="Explain the risk drivers, business impact, and why this remediation path is recommended..."
              />
            </div>

            <div className="flex flex-wrap gap-2">
              <Button
                onClick={handleSaveRecommendation}
                disabled={!canSave || isSaving}
              >
                {isSaving ? (
                  <LoaderCircle className="size-4 animate-spin" />
                ) : (
                  <Save className="size-4" />
                )}
                {isSaving
                  ? "Saving..."
                  : currentRecommendation
                    ? "Update recommendation"
                    : "Save recommendation"}
              </Button>
            </div>
          </CardContent>
        </Card>

        <PatchAssessmentPanel
          assessment={data.patchAssessment}
          canRequest={true}
          onRequest={() => void handleRequestAssessment()}
          requesting={requestingAssessment}
        />
      </div>

      <section className="space-y-3">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <h2 className="text-lg font-semibold">Open vulnerabilities</h2>
            <p className="text-sm text-muted-foreground">
              Highest-risk drivers first. Showing{" "}
              {topDrivers.length.toLocaleString()} of{" "}
              {vulnerabilities.length.toLocaleString()}
              {hiddenDriverCount > 0
                ? `, with ${hiddenDriverCount.toLocaleString()} more in the full case`
                : ""}
              .
            </p>
          </div>
        </div>
        <div className="overflow-x-auto rounded-lg border border-border/70 bg-card">
          <table className="min-w-[760px] divide-y divide-border/70 text-sm">
            <thead className="bg-muted/35 text-left text-xs uppercase tracking-[0.12em] text-muted-foreground">
              <tr>
                <th className="px-4 py-3">Vulnerability</th>
                <th className="px-4 py-3">Severity</th>
                <th className="px-4 py-3">Threats</th>
                <th className="px-4 py-3">Scope</th>
                <th className="px-4 py-3"></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border/60">
              {topDrivers.map((vulnerability) => (
                <tr
                  key={vulnerability.vulnerabilityId}
                  className="transition-colors hover:bg-muted/25"
                >
                  <td className="px-4 py-3">
                    <div className="font-medium">
                      {vulnerability.externalId}
                    </div>
                    <div className="line-clamp-1 max-w-xl text-xs text-muted-foreground">
                      {vulnerability.title}
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <span
                      className={cn(
                        "inline-flex rounded-full border px-2 py-0.5 text-xs font-medium",
                        toneBadge(
                          severityTone(
                            vulnerability.effectiveSeverity ??
                              vulnerability.vendorSeverity,
                          ),
                        ),
                      )}
                    >
                      {vulnerability.effectiveSeverity ??
                        vulnerability.vendorSeverity}
                      {vulnerability.effectiveScore != null
                        ? ` ${vulnerability.effectiveScore.toFixed(1)}`
                        : ""}
                    </span>
                  </td>
                  <td className="px-4 py-3">
                    <ThreatBadges vulnerability={vulnerability} />
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">
                    {vulnerability.affectedDeviceCount} devices
                  </td>
                  <td className="px-4 py-3 text-right">
                    <Button
                      variant="ghost"
                      size="sm"
                      aria-label={`Open details for ${vulnerability.externalId}`}
                      onClick={() => setSelectedVulnerability(vulnerability)}
                    >
                      Details
                    </Button>
                  </td>
                </tr>
              ))}
              {topDrivers.length === 0 ? (
                <tr>
                  <td
                    colSpan={5}
                    className="px-4 py-8 text-center text-muted-foreground"
                  >
                    No open vulnerabilities are linked to this remediation case.
                  </td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>
      </section>

      <VulnerabilityEssentialsSheet
        vulnerability={selectedVulnerability}
        open={selectedVulnerability !== null}
        onOpenChange={(open) => {
          if (!open) setSelectedVulnerability(null);
        }}
      />

      <WorkNotesSection caseId={caseId} />
    </section>
  );
}

function isDecisionContextQueryForCase(queryKey: readonly unknown[], caseId: string) {
  const namespace = queryKey[0]
  return (
    (namespace === 'security-analyst-workbench' || namespace === 'remediation-case')
    && queryKey[queryKey.length - 1] === caseId
  )
}

function buildMetricRailItems({
  data,
  highestRiskDriver,
  highestRiskDriverDetail,
  threatDriverCount,
}: {
  data: DecisionContext
  highestRiskDriver: DecisionVuln | null
  highestRiskDriverDetail: string
  threatDriverCount: number
}): MetricRailItem[] {
  return [
    {
      eyebrow: 'Open vulns',
      value: data.summary.totalVulnerabilities.toLocaleString(),
      sub: `${data.summary.criticalCount} critical · ${data.summary.highCount} high`,
    },
    {
      eyebrow: 'Affected',
      value: data.workflow.affectedDeviceCount.toLocaleString(),
      sub: `${data.workflow.affectedOwnerTeamCount} owner teams`,
    },
    {
      eyebrow: 'Owner',
      value: data.softwareOwnerTeamName ?? 'Default Team',
      sub: formatSoftwareOwnerRoutingDetail(
        data.softwareOwnerTeamName,
        data.softwareOwnerAssignmentSource,
      ),
    },
    {
      eyebrow: 'SLA',
      value: data.sla?.slaStatus ? startCase(data.sla.slaStatus) : 'Not set',
      sub: data.sla?.dueDate ? `Due ${formatNullableDateTime(data.sla.dueDate)}` : 'No due date',
      tone: getSlaTone(data.sla?.slaStatus),
      after: data.businessLabels.length > 0 ? (
        <>
          {data.businessLabels.map((label) => (
            <span
              key={label.id}
              className="inline-flex min-w-0 items-center gap-1 rounded-full border border-border/70 px-1.5 py-0.5 text-[10px] leading-none"
              title={`${label.weightCategory} business value, ${label.riskWeight.toFixed(1)}x risk weight`}
            >
              <span
                className="size-1.5 shrink-0 rounded-full border border-border/60"
                style={{ backgroundColor: label.color ?? 'transparent' }}
              />
              <span className="truncate font-medium">{label.name}</span>
              <span className="text-muted-foreground">{label.affectedDeviceCount}</span>
            </span>
          ))}
        </>
      ) : undefined,
    },
    {
      eyebrow: 'Top driver',
      eyebrowPrefix: highestRiskDriver ? '⚠' : undefined,
      value: highestRiskDriver?.externalId ?? 'None',
      sub: highestRiskDriverDetail,
      tone: highestRiskDriver ? 'danger' : 'default',
      mono: !!highestRiskDriver,
    },
    {
      eyebrow: 'Signals',
      value: threatDriverCount.toLocaleString(),
      sub: 'KEV, public exploit, or active alert',
      tone: threatDriverCount > 0 ? 'danger' : 'default',
    },
  ]
}

function getSlaTone(status: string | null | undefined): MetricRailTone {
  const normalized = status?.toLowerCase() ?? ''
  if (normalized.includes('breach') || normalized.includes('overdue')) return 'danger'
  if (normalized.includes('risk') || normalized.includes('due')) return 'warning'
  if (normalized.includes('track') || normalized.includes('met')) return 'success'
  return 'default'
}

function formatSeverityScore(severity: string, score: number | null | undefined) {
  return score == null ? severity : `${severity} ${score.toFixed(1)}`
}

function ThreatIntelBrief({
  threatIntel,
  isGenerating,
  onGenerate,
}: {
  threatIntel: ThreatIntel
  isGenerating: boolean
  onGenerate: () => void
}) {
  const [fullscreenOpen, setFullscreenOpen] = useState(false)
  const summaryLineCount = threatIntel.summary?.split(/\r?\n/).length ?? 0
  const hasLongSummary = !!threatIntel.summary
    && (threatIntel.summary.length > LONG_THREAT_INTEL_SUMMARY_LENGTH
      || summaryLineCount > LONG_THREAT_INTEL_SUMMARY_LINES)

  return (
    <>
      <Card className="shadow-none">
        <CardHeader>
          <div className="flex items-center justify-between gap-2">
            <CardTitle className="flex items-center gap-2">
              {isGenerating ? <LoaderCircle className="size-4 animate-spin text-primary" /> : <Bot className="size-4 text-primary" />}
              Threat intelligence
              {hasLongSummary ? (
                <Button
                  type="button"
                  variant="ghost"
                  size="icon-sm"
                  aria-label="View threat intelligence in fullscreen"
                  onClick={() => setFullscreenOpen(true)}
                  className="text-muted-foreground"
                >
                  <Maximize2 className="size-4" />
                </Button>
              ) : null}
            </CardTitle>
            {threatIntel.summary && threatIntel.canGenerate ? (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={onGenerate}
                disabled={isGenerating}
                className="text-xs text-muted-foreground"
              >
                {isGenerating ? 'Updating...' : 'Update threat intel'}
              </Button>
            ) : null}
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {threatIntel.summary ? (
            <>
              <div className="max-h-80 overflow-y-auto pr-2">
                <MarkdownViewer content={threatIntel.summary} />
              </div>
              <p className="text-xs text-muted-foreground">
                Generated {formatNullableDateTime(threatIntel.generatedAt)}
                {threatIntel.profileName ? ` · ${threatIntel.profileName}` : null}
              </p>
            </>
          ) : isGenerating ? (
            <p className="text-sm text-muted-foreground">
              Retrieving threat intelligence. This may take a moment.
            </p>
          ) : threatIntel.canGenerate ? (
            <div className="space-y-3">
              <p className="text-sm text-muted-foreground">
                Generate a threat intelligence summary for the top vulnerabilities — covering active exploitation, attack vectors, and mitigations.
              </p>
              <Button type="button" variant="outline" onClick={onGenerate} disabled={isGenerating}>
                Retrieve threat intel
              </Button>
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">
              {threatIntel.unavailableMessage ?? 'AI is not configured for this tenant.'}
            </p>
          )}
        </CardContent>
      </Card>

      <Dialog open={fullscreenOpen} onOpenChange={setFullscreenOpen}>
        <DialogContent className="w-[min(96vw,72rem)] max-w-[72rem] p-0 sm:max-w-[72rem]">
          <DialogHeader className="border-b border-border/60 px-5 py-4">
            <DialogTitle className="flex items-center gap-2">
              <Bot className="size-4 text-primary" />
              Threat intelligence
            </DialogTitle>
          </DialogHeader>
          <div className="max-h-[min(74vh,48rem)] overflow-y-auto px-5 py-4">
            {threatIntel.summary ? (
              <MarkdownViewer content={threatIntel.summary} />
            ) : null}
          </div>
        </DialogContent>
      </Dialog>
    </>
  )
}

function ThreatBadges({ vulnerability }: { vulnerability: DecisionVuln }) {
  const badges = [
    vulnerability.knownExploited ? 'KEV' : null,
    vulnerability.publicExploit ? 'Exploit' : null,
    vulnerability.activeAlert ? 'Alert' : null,
  ].filter(Boolean)

  if (badges.length === 0) {
    return <span className="text-xs text-muted-foreground">None</span>
  }

  return (
    <div className="flex flex-wrap gap-1">
      {badges.map((badge) => (
        <span key={badge} className={cn('rounded-full border px-1.5 py-0.5 text-[10px] font-medium', toneBadge('danger'))}>
          {badge}
        </span>
      ))}
    </div>
  )
}

function VulnerabilityEssentialsSheet({
  vulnerability,
  open,
  onOpenChange,
}: {
  vulnerability: DecisionVuln | null
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full overflow-y-auto border-l border-border/80 bg-card p-0 sm:max-w-xl">
        <SheetHeader className="border-b border-border/70 p-5">
          <SheetTitle>{vulnerability?.externalId ?? 'Vulnerability'}</SheetTitle>
          <SheetDescription className="line-clamp-2">{vulnerability?.title ?? ''}</SheetDescription>
        </SheetHeader>
        {vulnerability ? (
          <div className="space-y-5 p-5">
            <div className="flex flex-wrap gap-2">
              <span className={cn('inline-flex rounded-full border px-2 py-0.5 text-xs font-medium', toneBadge(severityTone(vulnerability.effectiveSeverity ?? vulnerability.vendorSeverity)))}>
                {vulnerability.effectiveSeverity ?? vulnerability.vendorSeverity}
                {vulnerability.effectiveScore != null ? ` ${vulnerability.effectiveScore.toFixed(1)}` : ''}
              </span>
              {vulnerability.epssScore != null ? (
                <Badge variant="outline">EPSS {(vulnerability.epssScore * 100).toFixed(1)}%</Badge>
              ) : null}
            </div>
            {vulnerability.description ? (
              <p className="text-sm leading-relaxed text-muted-foreground">{vulnerability.description}</p>
            ) : null}
            <div className="divide-y divide-border/60 rounded-lg border border-border/70">
              <SheetInfoRow label="First seen">{formatNullableDateTime(vulnerability.firstSeenAt)}</SheetInfoRow>
              <SheetInfoRow label="Affected devices">{vulnerability.affectedDeviceCount.toLocaleString()}</SheetInfoRow>
              <SheetInfoRow label="Affected versions">{vulnerability.affectedVersionCount.toLocaleString()}</SheetInfoRow>
              <SheetInfoRow label="Vendor score">{vulnerability.vendorScore != null ? vulnerability.vendorScore.toFixed(1) : '-'}</SheetInfoRow>
              <SheetInfoRow label="CVSS vector">
                {vulnerability.cvssVector ? <code className="break-all text-[11px] text-muted-foreground">{vulnerability.cvssVector}</code> : '-'}
              </SheetInfoRow>
            </div>
            <div className="rounded-lg border border-border/70 bg-background px-3 py-3">
              <div className="flex items-center gap-2 text-sm font-medium">
                <ShieldAlert className="size-4 text-muted-foreground" />
                Threat indicators
              </div>
              <div className="mt-3">
                <ThreatBadges vulnerability={vulnerability} />
              </div>
            </div>
          </div>
        ) : null}
      </SheetContent>
    </Sheet>
  )
}

function WorkNotesSection({ caseId }: { caseId: string }) {
  const { selectedTenantId } = useTenantScope()
  const queryClient = useQueryClient()
  const [content, setContent] = useState('')
  const [editingNoteId, setEditingNoteId] = useState<string | null>(null)
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null)

  const queryKey = useMemo(
    () => ['work-notes', selectedTenantId, 'remediations', caseId] as const,
    [selectedTenantId, caseId],
  )

  const notesQuery = useQuery({
    queryKey,
    queryFn: () => fetchWorkNotes({ data: { entityType: 'remediations', entityId: caseId } }),
  })

  const createMutation = useMutation({
    mutationFn: (markdown: string) =>
      createWorkNote({ data: { entityType: 'remediations', entityId: caseId, content: markdown } }),
    onSuccess: async () => {
      setContent('')
      await queryClient.invalidateQueries({ queryKey })
    },
  })

  const updateMutation = useMutation({
    mutationFn: ({ noteId, markdown }: { noteId: string; markdown: string }) =>
      updateWorkNote({ data: { noteId, content: markdown } }),
    onSuccess: async () => {
      setContent('')
      setEditingNoteId(null)
      await queryClient.invalidateQueries({ queryKey })
    },
  })

  const deleteMutation = useMutation({
    mutationFn: (noteId: string) => deleteWorkNote({ data: { noteId } }),
    onSuccess: async () => {
      if (editingNoteId === confirmDeleteId) {
        setEditingNoteId(null)
        setContent('')
      }
      setConfirmDeleteId(null)
      await queryClient.invalidateQueries({ queryKey })
    },
  })

  const notes = notesQuery.data ?? []
  const isEditing = editingNoteId !== null
  const isSubmitting = createMutation.isPending || updateMutation.isPending

  function beginEdit(note: WorkNote) {
    setEditingNoteId(note.id)
    setContent(note.content)
    setConfirmDeleteId(null)
  }

  function resetComposer() {
    setEditingNoteId(null)
    setContent('')
    setConfirmDeleteId(null)
  }

  function handleSubmit() {
    const trimmed = content.trim()
    if (!trimmed) return
    if (editingNoteId) {
      updateMutation.mutate({ noteId: editingNoteId, markdown: trimmed })
    } else {
      createMutation.mutate(trimmed)
    }
  }

  return (
    <section className="space-y-3">
      <div className="flex items-center gap-2">
        <NotebookPen className="size-4 text-muted-foreground" />
        <h2 className="text-lg font-semibold">Work notes</h2>
        {notesQuery.isFetching ? (
          <LoaderCircle className="size-3.5 animate-spin text-muted-foreground" />
        ) : notes.length > 0 ? (
          <span className="text-sm text-muted-foreground">({notes.length})</span>
        ) : null}
      </div>

      {notes.length === 0 && !notesQuery.isFetching ? (
        <div className="rounded-lg border border-dashed border-border/60 bg-card/50 px-4 py-6 text-center text-sm text-muted-foreground">
          No work notes yet. Capture useful context, links, or handover notes below.
        </div>
      ) : (
        <div className="space-y-3">
          {notes.map((note) => (
            <article key={note.id} className="rounded-lg border border-border/70 bg-card px-4 py-3">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="text-sm font-medium">{note.authorDisplayName}</p>
                  <p className="text-xs text-muted-foreground">
                    {formatDateTime(note.updatedAt ?? note.createdAt)}
                    {note.updatedAt ? ' · edited' : ''}
                  </p>
                </div>
                {note.canEdit || note.canDelete ? (
                  <div className="flex items-center gap-1">
                    {note.canEdit ? (
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        className="size-7"
                        onClick={() => beginEdit(note)}
                      >
                        <Pencil className="size-3.5" />
                      </Button>
                    ) : null}
                    {note.canDelete ? (
                      confirmDeleteId === note.id ? (
                        <Button
                          type="button"
                          variant="destructive"
                          size="sm"
                          onClick={() => deleteMutation.mutate(note.id)}
                          disabled={deleteMutation.isPending}
                        >
                          Confirm delete
                        </Button>
                      ) : (
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          className="size-7"
                          onClick={() => setConfirmDeleteId(note.id)}
                        >
                          <Trash2 className="size-3.5" />
                        </Button>
                      )
                    ) : null}
                  </div>
                ) : null}
              </div>
              <MarkdownViewer content={note.content} className="mt-3" />
            </article>
          ))}
        </div>
      )}

      <div className="rounded-lg border border-border/60 bg-card px-4 py-4 space-y-3">
        <p className="text-sm font-medium">
          {isEditing ? 'Edit note' : 'Add note'}
        </p>
        <p className="text-xs text-muted-foreground -mt-1">
          Markdown supported. Links, context, and handover notes are welcome.
        </p>
        <Textarea
          value={content}
          onChange={(e) => setContent(e.target.value)}
          rows={4}
          placeholder="Write a note — markdown and links are supported…"
          className="bg-background/60"
        />
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            onClick={handleSubmit}
            disabled={isSubmitting || content.trim().length === 0}
          >
            {isSubmitting ? <LoaderCircle className="size-4 animate-spin" /> : null}
            {isSubmitting ? 'Saving…' : isEditing ? 'Update note' : 'Add note'}
          </Button>
          {isEditing ? (
            <Button type="button" variant="outline" onClick={resetComposer}>
              Cancel
            </Button>
          ) : null}
        </div>
      </div>
    </section>
  )
}

function SheetInfoRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-4 px-3 py-2">
      <span className="shrink-0 text-xs text-muted-foreground">{label}</span>
      <span className="text-right text-sm">{children}</span>
    </div>
  )
}
