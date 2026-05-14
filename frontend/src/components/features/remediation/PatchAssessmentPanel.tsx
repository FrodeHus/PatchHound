import type { PatchAssessment } from '@/api/remediation.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { ChevronLeft, ChevronRight, LoaderCircle, ShieldAlert } from 'lucide-react'
import { useMemo, useState } from 'react'

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
    <div className="space-y-2">
      {(displayedAssessment.urgencyTier === URGENCY_TIER.Emergency || emergencyAssessment) && (
        <div className="flex items-center gap-2 rounded-md bg-destructive/15 border border-destructive px-4 py-3 text-destructive font-semibold">
          <ShieldAlert className="h-5 w-5 shrink-0" />
          <span>Emergency patch required — Target SLA: {displayedAssessment.urgencyTargetSla ?? 'as soon as possible'}</span>
        </div>
      )}

      <Card>
        <CardHeader className="flex flex-row items-center justify-between pb-2">
          <div className="space-y-1">
            <CardTitle className="text-sm font-medium">Patch Priority Assessment</CardTitle>
            {orderedVulnerabilities.length > 1 ? (
              <Badge variant="outline">{assessedCount} of {orderedVulnerabilities.length} assessed</Badge>
            ) : null}
          </div>
          {canRequest && !isLoading && (
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                if (orderedVulnerabilities.length > 1) {
                  setSelecting(true)
                } else {
                  onRequest(displayedAssessment.vulnerabilityId ? [displayedAssessment.vulnerabilityId] : undefined)
                }
              }}
              disabled={requesting}
            >
              {requesting ? <LoaderCircle className="h-4 w-4 animate-spin mr-1" /> : null}
              {hasAssessment ? 'Re-assess' : 'Request assessment'}
            </Button>
          )}
        </CardHeader>

        <CardContent>
          {runningVulnerabilityIds.size > 0 && orderedVulnerabilities.length > 1 && (
            <div className="mb-4 space-y-2">
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
            <div className="space-y-3">
              <div className="space-y-2">
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
              <Button onClick={requestSelected} disabled={requesting || selectedCount === 0}>
                {requesting ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                Request {selectedCount} assessment{selectedCount === 1 ? '' : 's'}
              </Button>
            </div>
          )}

          {!showSelection && completedAssessments.length > 1 && (
            <div className="mb-4 flex items-center justify-between gap-3 rounded-md border border-border bg-muted/15 px-3 py-2">
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
            <div className="flex items-center gap-2 text-muted-foreground text-sm py-4">
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
            <div className="space-y-4 text-sm">
              <div className="flex flex-wrap gap-2">
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

              {displayedAssessment.recommendation && (
                <div>
                  <div className="font-medium mb-1">Recommendation</div>
                  <p className="text-muted-foreground">{displayedAssessment.recommendation}</p>
                </div>
              )}

              {displayedAssessment.urgencyReason && (
                <div>
                  <div className="font-medium mb-1">Urgency Reason</div>
                  <p className="text-muted-foreground">{displayedAssessment.urgencyReason}</p>
                </div>
              )}

              {displayedAssessment.summary && (
                <div>
                  <div className="font-medium mb-1">Summary</div>
                  <p className="text-muted-foreground">{displayedAssessment.summary}</p>
                </div>
              )}

              {displayedAssessment.similarVulnerabilities && displayedAssessment.similarVulnerabilities.length > 0 && (
                <div>
                  <div className="font-medium mb-1">Similar Vulnerabilities</div>
                  <ul className="list-disc list-inside text-muted-foreground space-y-1">
                    {displayedAssessment.similarVulnerabilities.map((v, i) => (
                      <li key={i}>{v}</li>
                    ))}
                  </ul>
                </div>
              )}

              {displayedAssessment.compensatingControlsUntilPatched && displayedAssessment.compensatingControlsUntilPatched.length > 0 && (
                <div>
                  <div className="font-medium mb-1">Compensating Controls</div>
                  <ul className="list-disc list-inside text-muted-foreground space-y-1">
                    {displayedAssessment.compensatingControlsUntilPatched.map((c, i) => (
                      <li key={i}>{c}</li>
                    ))}
                  </ul>
                </div>
              )}

              {displayedAssessment.references && displayedAssessment.references.length > 0 && (
                <div>
                  <div className="font-medium mb-1">References</div>
                  <ul className="list-disc list-inside text-muted-foreground space-y-1">
                    {displayedAssessment.references.map((r, i) => (
                      <li key={i}>
                        <a href={r} className="underline break-all" target="_blank" rel="noreferrer">{r}</a>
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {displayedAssessment.assessedAt && (
                <p className="text-xs text-muted-foreground">
                  Assessed {new Date(displayedAssessment.assessedAt).toLocaleString()}
                  {displayedAssessment.aiProfileName ? ` · ${displayedAssessment.aiProfileName}` : ''}
                </p>
              )}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  )
}

