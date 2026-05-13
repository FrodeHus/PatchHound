import type { PatchAssessment } from '@/api/remediation.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { LoaderCircle, ShieldAlert } from 'lucide-react'

type Props = {
  assessment: PatchAssessment
  canRequest: boolean
  onRequest: () => void
  requesting: boolean
}

type BadgeVariant = 'default' | 'secondary' | 'destructive' | 'outline'

function urgencyVariant(tier: string | null): BadgeVariant {
  switch (tier) {
    case 'emergency': return 'destructive'
    case 'as_soon_as_possible': return 'default'
    case 'normal_patch_window': return 'secondary'
    case 'low_priority': return 'outline'
    default: return 'secondary'
  }
}

function urgencyLabel(tier: string | null): string {
  switch (tier) {
    case 'emergency': return 'Emergency'
    case 'as_soon_as_possible': return 'As Soon As Possible'
    case 'normal_patch_window': return 'Normal Patch Window'
    case 'low_priority': return 'Low Priority'
    default: return tier ?? 'Unknown'
  }
}

export function PatchAssessmentPanel({ assessment, canRequest, onRequest, requesting }: Props) {
  const isLoading = assessment.jobStatus === 'Pending' || assessment.jobStatus === 'Running'
  const hasAssessment = assessment.recommendation !== null

  return (
    <div className="space-y-2">
      {assessment.urgencyTier === 'emergency' && (
        <div className="flex items-center gap-2 rounded-md bg-destructive/15 border border-destructive px-4 py-3 text-destructive font-semibold">
          <ShieldAlert className="h-5 w-5 shrink-0" />
          <span>Emergency patch required — Target SLA: {assessment.urgencyTargetSla}</span>
        </div>
      )}

      <Card>
        <CardHeader className="flex flex-row items-center justify-between pb-2">
          <CardTitle className="text-sm font-medium">Patch Priority Assessment</CardTitle>
          {canRequest && !isLoading && (
            <Button variant="outline" size="sm" onClick={onRequest} disabled={requesting}>
              {requesting ? <LoaderCircle className="h-4 w-4 animate-spin mr-1" /> : null}
              {hasAssessment ? 'Re-assess' : 'Request assessment'}
            </Button>
          )}
        </CardHeader>

        <CardContent>
          {isLoading && (
            <div className="flex items-center gap-2 text-muted-foreground text-sm py-4">
              <LoaderCircle className="h-4 w-4 animate-spin" />
              Assessment in progress…
            </div>
          )}

          {!isLoading && !hasAssessment && assessment.jobStatus === 'Failed' && (
            <p className="text-sm text-destructive">Assessment failed. Request a new one above.</p>
          )}

          {!isLoading && !hasAssessment && assessment.jobStatus === 'None' && (
            <p className="text-sm text-muted-foreground">No assessment yet.</p>
          )}

          {hasAssessment && !isLoading && (
            <div className="space-y-4 text-sm">
              <div className="flex flex-wrap gap-2">
                <Badge variant={urgencyVariant(assessment.urgencyTier)}>
                  {urgencyLabel(assessment.urgencyTier)}
                </Badge>
                {assessment.urgencyTargetSla && (
                  <Badge variant="outline">SLA: {assessment.urgencyTargetSla}</Badge>
                )}
                {assessment.confidence && (
                  <Badge variant="secondary">Confidence: {assessment.confidence}</Badge>
                )}
              </div>

              {assessment.recommendation && (
                <div>
                  <div className="font-medium mb-1">Recommendation</div>
                  <p className="text-muted-foreground">{assessment.recommendation}</p>
                </div>
              )}

              {assessment.urgencyReason && (
                <div>
                  <div className="font-medium mb-1">Urgency Reason</div>
                  <p className="text-muted-foreground">{assessment.urgencyReason}</p>
                </div>
              )}

              {assessment.summary && (
                <div>
                  <div className="font-medium mb-1">Summary</div>
                  <p className="text-muted-foreground">{assessment.summary}</p>
                </div>
              )}

              {assessment.similarVulnerabilities &&
                assessment.similarVulnerabilities !== '[]' && (
                <div>
                  <div className="font-medium mb-1">Similar Vulnerabilities</div>
                  <ul className="list-disc list-inside text-muted-foreground space-y-1">
                    {tryParseArray(assessment.similarVulnerabilities).map((v, i) => (
                      <li key={i}>{typeof v === 'string' ? v : JSON.stringify(v)}</li>
                    ))}
                  </ul>
                </div>
              )}

              {assessment.compensatingControlsUntilPatched &&
                assessment.compensatingControlsUntilPatched !== '[]' && (
                <div>
                  <div className="font-medium mb-1">Compensating Controls</div>
                  <ul className="list-disc list-inside text-muted-foreground space-y-1">
                    {tryParseArray(assessment.compensatingControlsUntilPatched).map((c, i) => (
                      <li key={i}>{typeof c === 'string' ? c : JSON.stringify(c)}</li>
                    ))}
                  </ul>
                </div>
              )}

              {assessment.references && assessment.references !== '[]' && (
                <div>
                  <div className="font-medium mb-1">References</div>
                  <ul className="list-disc list-inside text-muted-foreground space-y-1">
                    {tryParseArray(assessment.references).map((r, i) => (
                      <li key={i}>
                        {typeof r === 'string'
                          ? <a href={r} className="underline break-all" target="_blank" rel="noreferrer">{r}</a>
                          : JSON.stringify(r)}
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {assessment.assessedAt && (
                <p className="text-xs text-muted-foreground">
                  Assessed {new Date(assessment.assessedAt).toLocaleString()}
                  {assessment.aiProfileName ? ` · ${assessment.aiProfileName}` : ''}
                </p>
              )}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  )
}

function tryParseArray(json: string | null): unknown[] {
  try {
    const parsed: unknown = JSON.parse(json ?? '[]')
    return Array.isArray(parsed) ? (parsed as unknown[]) : [parsed]
  } catch {
    return []
  }
}
