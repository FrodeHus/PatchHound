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

export function PatchAssessmentPanel({ assessment, canRequest, onRequest, requesting }: Props) {
  const isLoading = assessment.jobStatus === 'Pending' || assessment.jobStatus === 'Running'
  const hasAssessment = assessment.recommendation !== null

  return (
    <div className="space-y-2">
      {assessment.urgencyTier === URGENCY_TIER.Emergency && (
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

              {assessment.similarVulnerabilities && assessment.similarVulnerabilities.length > 0 && (
                <div>
                  <div className="font-medium mb-1">Similar Vulnerabilities</div>
                  <ul className="list-disc list-inside text-muted-foreground space-y-1">
                    {assessment.similarVulnerabilities.map((v, i) => (
                      <li key={i}>{v}</li>
                    ))}
                  </ul>
                </div>
              )}

              {assessment.compensatingControlsUntilPatched && assessment.compensatingControlsUntilPatched.length > 0 && (
                <div>
                  <div className="font-medium mb-1">Compensating Controls</div>
                  <ul className="list-disc list-inside text-muted-foreground space-y-1">
                    {assessment.compensatingControlsUntilPatched.map((c, i) => (
                      <li key={i}>{c}</li>
                    ))}
                  </ul>
                </div>
              )}

              {assessment.references && assessment.references.length > 0 && (
                <div>
                  <div className="font-medium mb-1">References</div>
                  <ul className="list-disc list-inside text-muted-foreground space-y-1">
                    {assessment.references.map((r, i) => (
                      <li key={i}>
                        <a href={r} className="underline break-all" target="_blank" rel="noreferrer">{r}</a>
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

