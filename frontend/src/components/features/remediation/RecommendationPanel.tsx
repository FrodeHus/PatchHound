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
  const currentRecommendation = recommendations[0] ?? null
  const [showForm, setShowForm] = useState(false)
  const [outcome, setOutcome] = useState(currentRecommendation?.recommendedOutcome ?? '')
  const [rationale, setRationale] = useState(currentRecommendation?.rationale ?? '')
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
      {currentRecommendation ? (
        <div className="rounded-lg border border-border/70 bg-background p-3 space-y-1.5">
          <div className="flex items-center gap-2">
            <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-medium ${toneBadge(outcomeTone(currentRecommendation.recommendedOutcome))}`}>
              {outcomeLabel(currentRecommendation.recommendedOutcome)}
            </span>
            {currentRecommendation.priorityOverride ? (
              <span className="text-xs text-muted-foreground">
                Priority: {currentRecommendation.priorityOverride}
              </span>
            ) : null}
          </div>
          <p className="text-sm">{currentRecommendation.rationale}</p>
          <p className="text-xs text-muted-foreground">
            {currentRecommendation.analystDisplayName ? `${currentRecommendation.analystDisplayName} · ` : ''}{formatDateTime(currentRecommendation.createdAt)}
          </p>
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">No analyst recommendation has been recorded yet.</p>
      )}

      {readOnly ? null : showForm ? (
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
              {submitting ? (currentRecommendation ? 'Updating...' : 'Submitting...') : (currentRecommendation ? 'Update recommendation' : 'Save recommendation')}
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => {
                setShowForm(false)
                setOutcome(currentRecommendation?.recommendedOutcome ?? '')
                setRationale(currentRecommendation?.rationale ?? '')
              }}
            >
              Cancel
            </Button>
          </div>
        </div>
      ) : (
        <Button
          variant="outline"
          size="sm"
          onClick={() => {
            setOutcome(currentRecommendation?.recommendedOutcome ?? '')
            setRationale(currentRecommendation?.rationale ?? '')
            setShowForm(true)
          }}
        >
          {currentRecommendation ? 'Update recommendation' : 'Add recommendation'}
        </Button>
      )}
    </div>
  )
}
