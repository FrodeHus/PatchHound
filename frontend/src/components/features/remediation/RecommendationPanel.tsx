import { useEffect, useState } from 'react'
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
  aiAnalystAssessment?: string | null
  aiRecommendedOutcome?: string | null
  aiRecommendedPriority?: string | null
  queryKey: readonly unknown[]
  readOnly?: boolean
  recommendationSeed?: {
    token: number
    outcome?: string | null
    rationale?: string | null
    priorityOverride?: string | null
  } | null
}

const OUTCOMES = [
  'ApprovedForPatching',
  'RiskAcceptance',
  'AlternateMitigation',
  'PatchingDeferred',
] as const

const PRIORITIES = ['Critical', 'High', 'Medium', 'Low'] as const

export function RecommendationPanel({
  tenantSoftwareId,
  workflowId,
  recommendations,
  aiAnalystAssessment,
  aiRecommendedOutcome,
  aiRecommendedPriority,
  queryKey,
  readOnly = false,
  recommendationSeed = null,
}: RecommendationPanelProps) {
  const queryClient = useQueryClient()
  const currentRecommendation = recommendations[0] ?? null
  const [showForm, setShowForm] = useState(false)
  const [outcome, setOutcome] = useState(currentRecommendation?.recommendedOutcome ?? '')
  const [rationale, setRationale] = useState(currentRecommendation?.rationale ?? '')
  const [priorityOverride, setPriorityOverride] = useState(currentRecommendation?.priorityOverride ?? '')
  const [submitting, setSubmitting] = useState(false)

  useEffect(() => {
    if (!recommendationSeed) {
      return
    }

    setOutcome(recommendationSeed.outcome ?? currentRecommendation?.recommendedOutcome ?? '')
    setRationale(recommendationSeed.rationale ?? currentRecommendation?.rationale ?? '')
    setPriorityOverride(recommendationSeed.priorityOverride ?? currentRecommendation?.priorityOverride ?? '')
    setShowForm(true)
  }, [recommendationSeed?.token, recommendationSeed, currentRecommendation?.priorityOverride, currentRecommendation?.rationale, currentRecommendation?.recommendedOutcome])

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
          priorityOverride: priorityOverride || undefined,
        },
      })
      await queryClient.invalidateQueries({ queryKey })
      setOutcome('')
      setRationale('')
      setPriorityOverride('')
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

      {aiAnalystAssessment || aiRecommendedPriority ? (
        <div className="rounded-lg border border-dashed border-border/70 bg-background/50 p-3 space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground">
              AI triage recommendation
            </span>
            {aiRecommendedPriority ? (
              <span className="rounded-full border border-border/70 bg-background/70 px-2 py-0.5 text-xs text-muted-foreground">
                {aiRecommendedPriority} priority
              </span>
            ) : null}
          </div>
          {aiAnalystAssessment ? (
            <p className="text-sm text-muted-foreground">{aiAnalystAssessment}</p>
          ) : null}
          {readOnly ? null : (
            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => {
                  setPriorityOverride(aiRecommendedPriority ?? '')
                  setOutcome((current) => current || aiRecommendedOutcome || '')
                  setRationale((current) => current || aiAnalystAssessment || '')
                  setShowForm(true)
                }}
              >
                Use in analyst note
              </Button>
            </div>
          )}
        </div>
      ) : null}

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
          <div className="space-y-2">
            <label className="text-sm font-medium">Final analyst priority</label>
            <Select value={priorityOverride || "none"} onValueChange={(value) => setPriorityOverride(value === 'none' ? '' : value ?? '')}>
              <SelectTrigger className="w-full">
                <SelectValue placeholder="No priority recommendation" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="none">No priority recommendation</SelectItem>
                {PRIORITIES.map((priority) => (
                  <SelectItem key={priority} value={priority}>
                    {priority}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
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
                setPriorityOverride(currentRecommendation?.priorityOverride ?? '')
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
            setPriorityOverride(currentRecommendation?.priorityOverride ?? '')
            setShowForm(true)
          }}
        >
          {currentRecommendation ? 'Update recommendation' : 'Add recommendation'}
        </Button>
      )}
    </div>
  )
}
