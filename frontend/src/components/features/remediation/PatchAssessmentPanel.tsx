import type { PatchAssessment } from '@/api/remediation.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { ChevronLeft, ChevronRight, LoaderCircle, ShieldAlert } from 'lucide-react'
import { useMemo, useState, type ReactNode } from 'react'
import { cn } from '@/lib/utils'

type Props = {
  assessment: PatchAssessment
  assessments?: PatchAssessment[]
  vulnerabilities?: PatchAssessmentVulnerability[]
  canRequest: boolean
  onRequest: (vulnerabilityIds?: string[]) => void
  requesting: boolean
  requestingVulnerabilityIds?: string[]
}

export type PatchAssessmentVulnerability = {
  vulnerabilityId: string
  externalId: string
  title?: string | null
  severity: string
}

const URGENCY_TIER = {
  Emergency: 'emergency',
  AsSoonAsPossible: 'as_soon_as_possible',
  NormalPatchWindow: 'normal_patch_window',
  LowPriority: 'low_priority',
} as const

type BadgeVariant = 'default' | 'secondary' | 'destructive' | 'outline'

function urgencyVariant(tier: string | null): BadgeVariant {
  switch (tier) {
    case URGENCY_TIER.Emergency: return 'destructive'
    case URGENCY_TIER.AsSoonAsPossible: return 'default'
    case URGENCY_TIER.NormalPatchWindow: return 'secondary'
    case URGENCY_TIER.LowPriority: return 'outline'
    default: return 'secondary'
  }
}

function urgencyLabel(tier: string | null): string {
  switch (tier) {
    case URGENCY_TIER.Emergency: return 'Emergency'
    case URGENCY_TIER.AsSoonAsPossible: return 'As Soon As Possible'
    case URGENCY_TIER.NormalPatchWindow: return 'Normal Patch Window'
    case URGENCY_TIER.LowPriority: return 'Low Priority'
    default: return tier ?? 'Unknown'
  }
}

const severityRank: Record<string, number> = {
  Critical: 4,
  High: 3,
  Medium: 2,
  Low: 1,
}

function sortVulnerabilities(vulnerabilities: PatchAssessmentVulnerability[]) {
  return [...vulnerabilities].sort((a, b) => {
    const severityDelta = (severityRank[b.severity] ?? 0) - (severityRank[a.severity] ?? 0)
    return severityDelta !== 0 ? severityDelta : a.externalId.localeCompare(b.externalId)
  })
}

function assessmentHasContent(assessment: PatchAssessment): boolean {
  return assessment.recommendation !== null
}

function isRunning(assessment: PatchAssessment): boolean {
  return assessment.jobStatus === 'Pending' || assessment.jobStatus === 'Running'
}

function failureMessage(error: string | null): string {
  if (!error) return 'The assessment worker did not return an error message.'

  const normalized = error.toLowerCase()
  if (normalized.includes('malformed ai response') || normalized.includes('json payload')) {
    return 'The AI response was not valid JSON. This usually means the answer was cut off or the provider returned text outside the expected schema.'
  }

  if (normalized.includes('timeout') || normalized.includes('timed out') || normalized.includes('taskcanceled')) {
    return 'The AI provider timed out before the assessment completed.'
  }

  return 'The assessment worker reported an error.'
}

export function PatchAssessmentPanel({
  assessment,
  assessments,
  vulnerabilities,
  canRequest,
  onRequest,
  requesting,
  requestingVulnerabilityIds = [],
}: Props) {
  const orderedVulnerabilities = useMemo(
    () => sortVulnerabilities(vulnerabilities ?? []),
    [vulnerabilities],
  )
  const assessmentList = assessments?.length ? assessments : [assessment]
  const assessmentByVulnerabilityId = new Map(
    assessmentList
      .filter((item) => item.vulnerabilityId)
      .map((item) => [item.vulnerabilityId!, item]),
  )
  const completedAssessments = orderedVulnerabilities.length > 0
    ? orderedVulnerabilities
      .map((vulnerability) => assessmentByVulnerabilityId.get(vulnerability.vulnerabilityId))
      .filter((item): item is PatchAssessment => !!item && assessmentHasContent(item))
    : assessmentHasContent(assessment)
      ? [assessment]
      : []
  const fallbackDisplayedAssessment = completedAssessments[0] ?? assessment
  const [selectedIndex, setSelectedIndex] = useState(0)
  const [selecting, setSelecting] = useState(completedAssessments.length === 0 && orderedVulnerabilities.length > 1)
  const [selectedIds, setSelectedIds] = useState<Set<string>>(
    () => new Set(
      orderedVulnerabilities
        .filter((vulnerability) => vulnerability.severity === 'Critical')
        .map((vulnerability) => vulnerability.vulnerabilityId),
    ),
  )

  const activeIndex = Math.min(selectedIndex, Math.max(completedAssessments.length - 1, 0))
  const displayedAssessment = completedAssessments[activeIndex] ?? fallbackDisplayedAssessment
  const isLoading = isRunning(displayedAssessment)
  const hasAssessment = assessmentHasContent(displayedAssessment)
  const assessedCount = orderedVulnerabilities.filter((vulnerability) => {
    const item = assessmentByVulnerabilityId.get(vulnerability.vulnerabilityId)
    return item ? assessmentHasContent(item) : false
  }).length
  const runningVulnerabilityIds = new Set([
    ...assessmentList.filter(isRunning).map((item) => item.vulnerabilityId).filter((id): id is string => !!id),
    ...requestingVulnerabilityIds,
  ])
  const emergencyAssessment = completedAssessments.some((item) => item.urgencyTier === URGENCY_TIER.Emergency)
  const hasEmergencyAssessment = displayedAssessment.urgencyTier === URGENCY_TIER.Emergency || emergencyAssessment
  const showSelection = orderedVulnerabilities.length > 1 && selecting
  const selectedCount = selectedIds.size

  function toggleSelected(vulnerabilityId: string) {
    setSelectedIds((current) => {
      const next = new Set(current)
      if (next.has(vulnerabilityId)) {
        next.delete(vulnerabilityId)
      } else {
        next.add(vulnerabilityId)
      }
      return next
    })
  }

  function requestSelected() {
    const ids = orderedVulnerabilities
      .filter((vulnerability) => selectedIds.has(vulnerability.vulnerabilityId))
      .map((vulnerability) => vulnerability.vulnerabilityId)
    onRequest(ids)
    setSelecting(false)
  }

  return (
    <Card
      className={cn(
        'h-[34rem] min-h-0',
        hasEmergencyAssessment && 'border-t-2 border-t-destructive',
      )}
    >
      <CardHeader className="flex shrink-0 flex-row items-center justify-between pb-2">
          <div className="space-y-1">
            <CardTitle className="flex items-center gap-2 text-sm font-medium">
              {hasEmergencyAssessment ? <ShieldAlert className="h-4 w-4 text-destructive" /> : null}
              Patch Priority Assessment
            </CardTitle>
            {orderedVulnerabilities.length > 1 ? (
              <Badge variant="outline">{assessedCount} of {orderedVulnerabilities.length} assessed</Badge>
            ) : null}
          </div>
          {canRequest && !isLoading && (
            showSelection ? (
              <div className="flex items-center gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={requestSelected}
                  disabled={requesting || selectedCount === 0}
                >
                  {requesting ? <LoaderCircle className="h-4 w-4 animate-spin mr-1" /> : null}
                  Request assessment
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => setSelecting(false)}
                  disabled={requesting}
                >
                  Cancel
                </Button>
              </div>
            ) : (
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  if (orderedVulnerabilities.length > 1) {
                    setSelecting(true)
                  } else {
                    const vulnerabilityId = displayedAssessment.vulnerabilityId ?? orderedVulnerabilities[0]?.vulnerabilityId
                    onRequest(vulnerabilityId ? [vulnerabilityId] : undefined)
                  }
                }}
                disabled={requesting}
              >
                {requesting ? <LoaderCircle className="h-4 w-4 animate-spin mr-1" /> : null}
                {hasAssessment ? 'Re-assess' : 'Request assessment'}
              </Button>
            )
          )}
      </CardHeader>

      <CardContent className="min-h-0 flex-1 overflow-hidden">
        <div className="flex h-full min-h-0 flex-col">
          {runningVulnerabilityIds.size > 0 && orderedVulnerabilities.length > 1 && (
            <div className="mb-4 max-h-28 shrink-0 space-y-2 overflow-y-auto pr-1">
              {orderedVulnerabilities
                .filter((vulnerability) => runningVulnerabilityIds.has(vulnerability.vulnerabilityId))
                .map((vulnerability) => (
                  <div key={vulnerability.vulnerabilityId} className="rounded-md border border-border bg-muted/20 p-3">
                    <div className="flex items-center justify-between gap-3 text-sm">
                      <span className="font-medium">{vulnerability.externalId}</span>
                      <span className="text-muted-foreground">Assessment running</span>
                    </div>
                    <div className="mt-2 h-1 overflow-hidden rounded-full bg-muted">
                      <div className="h-full w-1/3 animate-pulse rounded-full bg-primary" />
                    </div>
                  </div>
                ))}
            </div>
          )}

          {showSelection && (
            <div className="flex min-h-0 flex-1 flex-col space-y-3">
              <div className="min-h-0 flex-1 space-y-2 overflow-y-auto pr-1">
                {orderedVulnerabilities.map((vulnerability) => (
                  <label
                    key={vulnerability.vulnerabilityId}
                    className="flex cursor-pointer items-start gap-3 rounded-md border border-border bg-muted/15 p-3 text-sm"
                  >
                    <input
                      type="checkbox"
                      className="mt-1"
                      checked={selectedIds.has(vulnerability.vulnerabilityId)}
                      onChange={() => toggleSelected(vulnerability.vulnerabilityId)}
                    />
                    <span className="min-w-0 flex-1">
                      <span className="flex flex-wrap items-center gap-2">
                        <span className="font-medium">{vulnerability.externalId}</span>
                        <Badge variant={vulnerability.severity === 'Critical' ? 'destructive' : 'outline'}>
                          {vulnerability.severity}
                        </Badge>
                      </span>
                      {vulnerability.title ? (
                        <span className="mt-1 block text-muted-foreground">{vulnerability.title}</span>
                      ) : null}
                    </span>
                  </label>
                ))}
              </div>
              <p className="shrink-0 text-xs text-muted-foreground">
                {selectedCount} {selectedCount === 1 ? 'vulnerability' : 'vulnerabilities'} selected for assessment.
              </p>
            </div>
          )}

          {!showSelection && completedAssessments.length > 1 && (
            <div className="mb-4 flex shrink-0 items-center justify-between gap-3 rounded-md border border-border bg-muted/15 px-3 py-2">
              <Button
                variant="ghost"
                size="sm"
                onClick={() => setSelectedIndex(Math.max(activeIndex - 1, 0))}
                disabled={activeIndex === 0}
              >
                <ChevronLeft className="h-4 w-4" />
                Previous
              </Button>
              <span className="text-sm text-muted-foreground">
                {activeIndex + 1} of {completedAssessments.length}
              </span>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => setSelectedIndex(Math.min(activeIndex + 1, completedAssessments.length - 1))}
                disabled={activeIndex >= completedAssessments.length - 1}
              >
                Next
                <ChevronRight className="h-4 w-4" />
              </Button>
            </div>
          )}

          {!showSelection && isLoading && (
            <div className="flex items-center gap-2 py-4 text-sm text-muted-foreground">
              <LoaderCircle className="h-4 w-4 animate-spin" />
              Assessment in progress…
            </div>
          )}

          {!showSelection && !isLoading && !hasAssessment && displayedAssessment.jobStatus === 'Failed' && (
            <div className="rounded-md border border-destructive/40 bg-destructive/10 p-3 text-sm">
              <div className="font-medium text-destructive">Assessment failed</div>
              <p className="mt-1 text-muted-foreground">{failureMessage(displayedAssessment.jobError)}</p>
              {displayedAssessment.jobError && (
                <p className="mt-2 break-words font-mono text-xs text-destructive">{displayedAssessment.jobError}</p>
              )}
            </div>
          )}

          {!showSelection && !isLoading && !hasAssessment && displayedAssessment.jobStatus === 'None' && (
            <p className="text-sm text-muted-foreground">No assessment yet.</p>
          )}

          {!showSelection && hasAssessment && !isLoading && (
            <div className="flex min-h-0 flex-1 flex-col gap-4 text-sm">
              <div className="flex shrink-0 flex-wrap gap-2">
                <Badge variant={urgencyVariant(displayedAssessment.urgencyTier)}>
                  {urgencyLabel(displayedAssessment.urgencyTier)}
                </Badge>
                {displayedAssessment.urgencyTargetSla && (
                  <Badge variant="outline">SLA: {displayedAssessment.urgencyTargetSla}</Badge>
                )}
                {displayedAssessment.confidence && (
                  <Badge variant="secondary">Confidence: {displayedAssessment.confidence}</Badge>
                )}
              </div>
              {displayedAssessment.recommendation ? (
                <p className="shrink-0 text-sm text-muted-foreground">{displayedAssessment.recommendation}</p>
              ) : null}

              <Tabs defaultValue="urgency" className="min-h-0 flex-1">
                <TabsList className="grid h-auto w-full grid-cols-3 overflow-hidden rounded-lg bg-muted/50 p-1 md:grid-cols-5">
                  <TabsTrigger value="urgency">Urgency</TabsTrigger>
                  <TabsTrigger value="summary">Summary</TabsTrigger>
                  <TabsTrigger value="similar">Similar</TabsTrigger>
                  <TabsTrigger value="controls">Controls</TabsTrigger>
                  <TabsTrigger value="references">References</TabsTrigger>
                </TabsList>

                <AssessmentTabContent value="urgency">
                  <div className="space-y-3">
                    <div>
                      <div className="mb-1 font-medium">Urgency Reason</div>
                      <p className="text-muted-foreground">{displayedAssessment.urgencyReason ?? 'No urgency rationale was returned.'}</p>
                    </div>
                    {displayedAssessment.urgencyTargetSla ? (
                      <div>
                        <div className="mb-1 font-medium">Target SLA</div>
                        <p className="text-muted-foreground">{displayedAssessment.urgencyTargetSla}</p>
                      </div>
                    ) : null}
                  </div>
                </AssessmentTabContent>

                <AssessmentTabContent value="summary">
                  <p className="text-muted-foreground">{displayedAssessment.summary ?? 'No summary was returned.'}</p>
                </AssessmentTabContent>

                <AssessmentTabContent value="similar">
                  <StringList
                    items={displayedAssessment.similarVulnerabilities}
                    emptyText="No similar vulnerabilities were returned."
                    linkItems={false}
                  />
                </AssessmentTabContent>

                <AssessmentTabContent value="controls">
                  <StringList
                    items={displayedAssessment.compensatingControlsUntilPatched}
                    emptyText="No compensating controls were returned."
                    linkItems={false}
                  />
                </AssessmentTabContent>

                <AssessmentTabContent value="references">
                  <StringList
                    items={displayedAssessment.references}
                    emptyText="No references were returned."
                    linkItems={true}
                  />
                </AssessmentTabContent>
              </Tabs>

              {displayedAssessment.assessedAt && (
                <p className="text-xs text-muted-foreground">
                  Assessed {new Date(displayedAssessment.assessedAt).toLocaleString()}
                  {displayedAssessment.aiProfileName ? ` · ${displayedAssessment.aiProfileName}` : ''}
                </p>
              )}
            </div>
          )}
        </div>
      </CardContent>
    </Card>
  )
}

function AssessmentTabContent({
  value,
  children,
}: {
  value: string
  children: ReactNode
}) {
  return (
    <TabsContent
      value={value}
      className="mt-2 min-h-0 overflow-y-auto rounded-md border border-border bg-muted/15 p-3"
    >
      {children}
    </TabsContent>
  )
}

function StringList({
  items,
  emptyText,
  linkItems,
}: {
  items: IReadonlyStringList
  emptyText: string
  linkItems: boolean
}) {
  if (!items || items.length === 0) {
    return <p className="text-muted-foreground">{emptyText}</p>
  }

  return (
    <ul className="list-inside list-disc space-y-1 text-muted-foreground">
      {items.map((item, index) => (
        <li key={index}>
          {linkItems ? (
            <a href={item} className="underline break-all" target="_blank" rel="noreferrer">{item}</a>
          ) : item}
        </li>
      ))}
    </ul>
  )
}

type IReadonlyStringList = ReadonlyArray<string> | null

