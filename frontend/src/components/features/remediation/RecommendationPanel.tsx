import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import type { AnalystRecommendation } from '@/api/remediation.schemas'
import { addRecommendation } from '@/api/remediation.functions'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { toneBadge } from '@/lib/tone-classes'
import { formatDateTime } from '@/lib/formatting'
import { outcomeLabel, outcomeTone } from './remediation-utils'

type RecommendationPanelProps = {
  tenantSoftwareId: string
  workflowId?: string | null
  recommendations: AnalystRecommendation[]
  queryKey: readonly unknown[]
  readOnly?: boolean
}

const OUTCOMES = [
  'ApprovedForPatching',
  'RiskAcceptance',
  'AlternateMitigation',
  'PatchingDeferred',
] as const

export function RecommendationPanel({
  tenantSoftwareId,
  workflowId,
  recommendations,
  queryKey,
  readOnly = false,
}: RecommendationPanelProps) {
  const queryClient = useQueryClient()
  const [showForm, setShowForm] = useState(false)
  const [outcome, setOutcome] = useState('')
  const [rationale, setRationale] = useState('')
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit() {
    if (!outcome || !rationale.trim()) return
    setSubmitting(true)
    try {
      await addRecommendation({
        data: {
          tenantSoftwareId,
          workflowId,
          recommendedOutcome: outcome,
          rationale: rationale.trim(),
        },
      })
      await queryClient.invalidateQueries({ queryKey })
      setOutcome('')
      setRationale('')
      setShowForm(false)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="space-y-3">
      {recommendations.length > 0 ? (
        <div className="space-y-2">
          {recommendations.map((rec) => (
            <div
              key={rec.id}
              className="rounded-lg border border-border/70 bg-background p-3 space-y-1.5"
            >
              <div className="flex items-center gap-2">
                <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-medium ${toneBadge(outcomeTone(rec.recommendedOutcome))}`}>
                  {outcomeLabel(rec.recommendedOutcome)}
                </span>
                {rec.priorityOverride ? (
                  <span className="text-xs text-muted-foreground">
                    Priority: {rec.priorityOverride}
                  </span>
                ) : null}
              </div>
              <p className="text-sm">{rec.rationale}</p>
              <p className="text-xs text-muted-foreground">
                {rec.analystDisplayName ? `${rec.analystDisplayName} · ` : ''}{formatDateTime(rec.createdAt)}
              </p>
            </div>
          ))}
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">No analyst recommendations yet.</p>
      )}

      {readOnly ? (
        <p className="text-xs text-muted-foreground">
          You can review analyst recommendations here, but only the current stage owner can add or change them.
        </p>
      ) : showForm ? (
        <div className="space-y-3 rounded-lg border border-border/70 bg-background p-3">
          <div className="space-y-2">
            <label className="text-sm font-medium">Recommended Outcome</label>
            <Select value={outcome} onValueChange={(v) => setOutcome(v ?? '')}>
              <SelectTrigger className="w-full">
                <SelectValue placeholder="Select outcome..." />
              </SelectTrigger>
              <SelectContent>
                {OUTCOMES.map((o) => (
                  <SelectItem key={o} value={o}>
                    {outcomeLabel(o)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-2">
            <label className="text-sm font-medium">Rationale</label>
            <Textarea
              value={rationale}
              onChange={(e) => setRationale(e.target.value)}
              placeholder="Explain your recommendation..."
              rows={3}
            />
          </div>
          <div className="flex gap-2">
            <Button
              onClick={handleSubmit}
              disabled={!outcome || !rationale.trim() || submitting}
              size="sm"
            >
              {submitting ? 'Submitting...' : 'Submit'}
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => { setShowForm(false); setOutcome(''); setRationale('') }}
            >
              Cancel
            </Button>
          </div>
        </div>
      ) : (
        <Button variant="outline" size="sm" onClick={() => setShowForm(true)}>
          Add Recommendation
        </Button>
      )}
    </div>
  )
}
