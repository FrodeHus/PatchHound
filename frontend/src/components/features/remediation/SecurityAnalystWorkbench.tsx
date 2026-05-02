import { useMemo, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { Bot, ExternalLink, LoaderCircle, SearchCheck, ShieldAlert } from 'lucide-react'
import type { DecisionContext, DecisionVuln } from '@/api/remediation.schemas'
import { addRecommendation, generateRemediationAiSummary } from '@/api/remediation.functions'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Textarea } from '@/components/ui/textarea'
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

export function SecurityAnalystWorkbench({ data, caseId, queryKey }: SecurityAnalystWorkbenchProps) {
  const queryClient = useQueryClient()
  const currentRecommendation = data.recommendations[0] ?? null
  const [selectedVulnerability, setSelectedVulnerability] = useState<DecisionVuln | null>(null)
  const [recommendedOutcome, setRecommendedOutcome] = useState(currentRecommendation?.recommendedOutcome ?? data.aiSummary.recommendedOutcome ?? '')
  const [rationale, setRationale] = useState(currentRecommendation?.rationale ?? data.aiSummary.analystAssessment ?? '')
  const [priorityOverride, setPriorityOverride] = useState(currentRecommendation?.priorityOverride ?? data.aiSummary.recommendedPriority ?? '')
  const [isSaving, setIsSaving] = useState(false)
  const [isGeneratingAi, setIsGeneratingAi] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const vulnerabilities = data.openVulnerabilities.length > 0 ? data.openVulnerabilities : data.topVulnerabilities
  const topDrivers = useMemo(() => vulnerabilities.slice(0, 4), [vulnerabilities])
  const canSave = recommendedOutcome !== '' && rationale.trim().length > 0
  const displaySoftwareName = startCase(data.softwareName)
  const softwareIdentity = data.softwareVendor
    && !displaySoftwareName.toLowerCase().startsWith(data.softwareVendor.toLowerCase())
    ? `${data.softwareVendor} ${displaySoftwareName}`
    : displaySoftwareName

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
      await queryClient.invalidateQueries({ queryKey })
    } catch (err) {
      setError(getApiErrorMessage(err, 'Unable to save the analyst recommendation.'))
    } finally {
      setIsSaving(false)
    }
  }

  async function handleGenerateAiSummary() {
    setIsGeneratingAi(true)
    setError(null)
    try {
      await generateRemediationAiSummary({ data: { caseId } })
      await queryClient.invalidateQueries({ queryKey })
    } catch (err) {
      setError(getApiErrorMessage(err, 'Unable to request AI risk guidance.'))
    } finally {
      setIsGeneratingAi(false)
    }
  }

  function useAiDraft() {
    setRecommendedOutcome((current) => current || data.aiSummary.recommendedOutcome || '')
    setRationale((current) => current || data.aiSummary.analystAssessment || '')
    setPriorityOverride((current) => current || data.aiSummary.recommendedPriority || '')
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
              <Badge variant="outline">{data.workflowState.currentStageLabel}</Badge>
            </div>
            <p className="max-w-4xl text-sm leading-relaxed text-muted-foreground">
              {data.softwareDescription
                ?? 'Review the open exposure and capture the recommendation the asset owner will use to decide the remediation path.'}
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
      </header>

      <div className="grid gap-3 md:grid-cols-4">
        <WorkbenchMetric label="Open vulnerabilities" value={data.summary.totalVulnerabilities.toLocaleString()} detail={`${data.summary.criticalCount} critical · ${data.summary.highCount} high`} />
        <WorkbenchMetric label="Affected devices" value={data.workflow.affectedDeviceCount.toLocaleString()} detail={`${data.workflow.affectedOwnerTeamCount} owner teams`} />
        <WorkbenchMetric label="Owner routing" value={data.softwareOwnerTeamName ?? 'Default Team'} detail={formatSoftwareOwnerRoutingDetail(data.softwareOwnerTeamName, data.softwareOwnerAssignmentSource)} />
        <WorkbenchMetric label="SLA" value={data.sla?.slaStatus ?? 'Not set'} detail={data.sla?.dueDate ? `Due ${formatNullableDateTime(data.sla.dueDate)}` : 'No due date'} />
      </div>

      {data.businessLabels.length > 0 ? (
        <div className="flex flex-wrap gap-2">
          {data.businessLabels.map((label) => (
            <span
              key={label.id}
              className="inline-flex items-center gap-2 rounded-full border border-border/70 bg-background px-2.5 py-1 text-xs"
              title={`${label.weightCategory} business value, ${label.riskWeight.toFixed(1)}x risk weight`}
            >
              <span
                className="size-2 rounded-full border border-border/60"
                style={{ backgroundColor: label.color ?? 'transparent' }}
              />
              {label.name}
              <span className="text-muted-foreground">({label.affectedDeviceCount})</span>
            </span>
          ))}
        </div>
      ) : null}

      {error ? (
        <div role="alert" className="rounded-lg border border-destructive/35 bg-destructive/8 px-4 py-3 text-sm text-destructive">
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
                  <span className={cn('inline-flex rounded-full border px-2 py-0.5 text-xs font-medium', toneBadge(outcomeTone(currentRecommendation.recommendedOutcome)))}>
                    {outcomeLabel(currentRecommendation.recommendedOutcome)}
                  </span>
                  {currentRecommendation.priorityOverride ? (
                    <span className="text-xs text-muted-foreground">{currentRecommendation.priorityOverride} priority</span>
                  ) : null}
                </div>
                <p className="mt-2 text-sm text-muted-foreground">
                  Saved {currentRecommendation.analystDisplayName ? `by ${currentRecommendation.analystDisplayName} ` : ''}{formatDateTime(currentRecommendation.createdAt)}
                </p>
              </div>
            ) : null}

            <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_180px]">
              <div className="space-y-2">
                <label className="text-sm font-medium">Recommended remediation</label>
                <Select value={recommendedOutcome} onValueChange={(value) => setRecommendedOutcome(value ?? '')}>
                  <SelectTrigger>
                    <SelectValue placeholder="Select action..." />
                  </SelectTrigger>
                  <SelectContent>
                    {OUTCOMES.map((outcome) => (
                      <SelectItem key={outcome} value={outcome}>{outcomeLabel(outcome)}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2">
                <label className="text-sm font-medium">Priority</label>
                <Select value={priorityOverride || 'none'} onValueChange={(value) => setPriorityOverride(value === 'none' ? '' : value ?? '')}>
                  <SelectTrigger>
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
              <label htmlFor="analyst-recommendation-rationale" className="text-sm font-medium">Recommendation rationale</label>
              <Textarea
                id="analyst-recommendation-rationale"
                value={rationale}
                onChange={(event) => setRationale(event.target.value)}
                rows={7}
                placeholder="Explain the risk drivers, business impact, and why this remediation path is recommended..."
              />
            </div>

            <div className="flex flex-wrap gap-2">
              <Button onClick={handleSaveRecommendation} disabled={!canSave || isSaving}>
                {isSaving ? 'Saving...' : currentRecommendation ? 'Update recommendation' : 'Save recommendation'}
              </Button>
              {(data.aiSummary.analystAssessment || data.aiSummary.recommendedOutcome || data.aiSummary.recommendedPriority) ? (
                <Button type="button" variant="outline" onClick={useAiDraft}>
                  Use AI draft
                </Button>
              ) : null}
            </div>
          </CardContent>
        </Card>

        <AiRiskBrief
          data={data}
          isGenerating={isGeneratingAi}
          onGenerate={() => void handleGenerateAiSummary()}
        />
      </div>

      <section className="space-y-3">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <h2 className="text-lg font-semibold">Open vulnerabilities</h2>
            <p className="text-sm text-muted-foreground">Highest-risk drivers first. Open a row for essentials without leaving the workbench.</p>
          </div>
        </div>
        <div className="overflow-hidden rounded-lg border border-border/70 bg-card">
          <table className="min-w-full divide-y divide-border/70 text-sm">
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
                <tr key={vulnerability.vulnerabilityId}>
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
                  <td className="px-4 py-3">
                    <ThreatBadges vulnerability={vulnerability} />
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">
                    {vulnerability.affectedDeviceCount} devices
                  </td>
                  <td className="px-4 py-3 text-right">
                    <Button variant="ghost" size="sm" onClick={() => setSelectedVulnerability(vulnerability)}>
                      Details
                    </Button>
                  </td>
                </tr>
              ))}
              {topDrivers.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-4 py-8 text-center text-muted-foreground">
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
        onOpenChange={(open) => { if (!open) setSelectedVulnerability(null) }}
      />
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

function AiRiskBrief({
  data,
  isGenerating,
  onGenerate,
}: {
  data: DecisionContext
  isGenerating: boolean
  onGenerate: () => void
}) {
  const hasAnalystGuidance = Boolean(data.aiSummary.analystAssessment || data.aiSummary.recommendedOutcome || data.aiSummary.recommendedPriority)
  const isWorking = data.aiSummary.status === 'Queued' || data.aiSummary.status === 'Generating'

  return (
    <Card className="shadow-none">
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          {isWorking ? <LoaderCircle className="size-4 animate-spin text-primary" /> : <Bot className="size-4 text-primary" />}
          AI risk brief
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {hasAnalystGuidance ? (
          <>
            {data.aiSummary.recommendedPriority ? (
              <div>
                <p className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Suggested priority</p>
                <p className="mt-1 text-sm font-medium">{data.aiSummary.recommendedPriority}</p>
              </div>
            ) : null}
            {data.aiSummary.recommendedOutcome ? (
              <div>
                <p className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Suggested action</p>
                <p className="mt-1 text-sm font-medium">{outcomeLabel(data.aiSummary.recommendedOutcome)}</p>
              </div>
            ) : null}
            {data.aiSummary.analystAssessment ? (
              <p className="whitespace-pre-line text-sm leading-relaxed text-muted-foreground">
                {data.aiSummary.analystAssessment}
              </p>
            ) : null}
            <p className="text-xs text-muted-foreground">
              Generated {formatNullableDateTime(data.aiSummary.generatedAt)}
            </p>
          </>
        ) : isWorking ? (
          <p className="text-sm text-muted-foreground">
            AI guidance is queued or generating. You can continue the analysis while it runs.
          </p>
        ) : data.aiSummary.canGenerate ? (
          <div className="space-y-3">
            <p className="text-sm text-muted-foreground">
              Ask AI for a concise analyst-oriented summary of the highest-risk open vulnerabilities.
            </p>
            <Button type="button" variant="outline" onClick={onGenerate} disabled={isGenerating}>
              {isGenerating ? 'Asking AI...' : 'Ask AI'}
            </Button>
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">
            {data.aiSummary.unavailableMessage ?? 'AI guidance is not available for this tenant.'}
          </p>
        )}
      </CardContent>
    </Card>
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

function SheetInfoRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-4 px-3 py-2">
      <span className="shrink-0 text-xs text-muted-foreground">{label}</span>
      <span className="text-right text-sm">{children}</span>
    </div>
  )
}
