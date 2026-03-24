import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { createDecision } from '@/api/remediation.functions'
import { outcomeLabel } from './remediation-utils'

type DecisionFormProps = {
  tenantSoftwareId: string
  queryKey: readonly unknown[]
}

const OUTCOMES = [
  'ApprovedForPatching',
  'RiskAcceptance',
  'AlternateMitigation',
  'PatchingDeferred',
] as const

const REQUIRES_JUSTIFICATION = new Set<string>([
  'RiskAcceptance',
  'AlternateMitigation',
  'PatchingDeferred',
])
const REQUIRES_EXPIRY = new Set<string>(['RiskAcceptance', 'AlternateMitigation'])
const REQUIRES_REEVALUATION = new Set<string>(['PatchingDeferred'])

export function DecisionForm({ tenantSoftwareId, queryKey }: DecisionFormProps) {
  const queryClient = useQueryClient()
  const [outcome, setOutcome] = useState('')
  const [justification, setJustification] = useState('')
  const [expiryDate, setExpiryDate] = useState('')
  const [reEvaluationDate, setReEvaluationDate] = useState('')
  const [submitting, setSubmitting] = useState(false)

  const needsJustification = REQUIRES_JUSTIFICATION.has(outcome)
  const needsExpiry = REQUIRES_EXPIRY.has(outcome)
  const needsReEvaluation = REQUIRES_REEVALUATION.has(outcome)

  const canSubmit =
    outcome !== '' &&
    (!needsJustification || justification.trim().length > 0) &&
    (!needsReEvaluation || reEvaluationDate !== '')

  async function handleSubmit() {
    if (!canSubmit) return
    setSubmitting(true)
    try {
      await createDecision({
        data: {
          tenantSoftwareId,
          outcome,
          justification: justification || undefined,
          expiryDate: expiryDate || undefined,
          reEvaluationDate: reEvaluationDate || undefined,
        },
      })
      await queryClient.invalidateQueries({ queryKey })
      setOutcome('')
      setJustification('')
      setExpiryDate('')
      setReEvaluationDate('')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <label className="text-sm font-medium">Outcome</label>
        <Select value={outcome} onValueChange={(v) => setOutcome(v ?? '')}>
          <SelectTrigger className="w-full">
            <SelectValue placeholder="Select remediation outcome..." />
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

      {needsJustification ? (
        <div className="space-y-2">
          <label className="text-sm font-medium">
            Justification <span className="text-tone-danger-foreground">*</span>
          </label>
          <Textarea
            value={justification}
            onChange={(e) => setJustification(e.target.value)}
            placeholder="Provide justification for this decision..."
            rows={3}
          />
        </div>
      ) : null}

      {needsExpiry ? (
        <div className="space-y-2">
          <label className="text-sm font-medium">Expiry Date</label>
          <Input
            type="date"
            value={expiryDate}
            onChange={(e) => setExpiryDate(e.target.value)}
          />
          <p className="text-xs text-muted-foreground">
            Optional. When the acceptance or mitigation expires.
          </p>
        </div>
      ) : null}

      {needsReEvaluation ? (
        <div className="space-y-2">
          <label className="text-sm font-medium">
            Re-evaluation Date <span className="text-tone-danger-foreground">*</span>
          </label>
          <Input
            type="date"
            value={reEvaluationDate}
            onChange={(e) => setReEvaluationDate(e.target.value)}
          />
          <p className="text-xs text-muted-foreground">
            When this deferral should be reassessed.
          </p>
        </div>
      ) : null}

      <Button
        onClick={handleSubmit}
        disabled={!canSubmit || submitting}
        className="w-full sm:w-auto"
      >
        {submitting ? 'Submitting...' : 'Submit Decision'}
      </Button>
    </div>
  )
}
