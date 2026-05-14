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
import { getApiErrorMessage } from '@/lib/api-errors'
import { outcomeLabel } from './remediation-utils'

type DecisionFormProps = {
  caseId: string
  queryKey: readonly unknown[]
  readOnly?: boolean
  initialOutcome?: string | null
  initialJustification?: string | null
  initialMaintenanceWindowDate?: string | null
  initialExpiryDate?: string | null
  initialReEvaluationDate?: string | null
  submitLabel?: string
  decisionSeed?: {
    token: number
    outcome?: string | null
    justification?: string | null
    maintenanceWindowDate?: string | null
    expiryDate?: string | null
    reEvaluationDate?: string | null
  } | null
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

export function DecisionForm({
  decisionSeed = null,
  initialMaintenanceWindowDate: _initialMaintenanceWindowDate = null,
  ...props
}: DecisionFormProps) {
  const initialOutcome = decisionSeed?.outcome ?? props.initialOutcome ?? null
  const initialJustification = decisionSeed?.justification ?? props.initialJustification ?? null
  const initialExpiryDate = decisionSeed?.expiryDate ?? props.initialExpiryDate ?? null
  const initialReEvaluationDate = decisionSeed?.reEvaluationDate ?? props.initialReEvaluationDate ?? null
  const formKey = [
    decisionSeed?.token ?? 'initial',
    initialOutcome ?? '',
    initialJustification ?? '',
    initialExpiryDate ?? '',
    initialReEvaluationDate ?? '',
  ].join('|')

  return (
    <DecisionFormFields
      key={formKey}
      {...props}
      initialOutcome={initialOutcome}
      initialJustification={initialJustification}
      initialExpiryDate={initialExpiryDate}
      initialReEvaluationDate={initialReEvaluationDate}
    />
  )
}

function DecisionFormFields({
  caseId,
  queryKey,
  readOnly = false,
  initialOutcome = null,
  initialJustification = null,
  initialExpiryDate = null,
  initialReEvaluationDate = null,
  submitLabel = 'Submit owner decision',
}: Omit<DecisionFormProps, 'decisionSeed' | 'initialMaintenanceWindowDate'>) {
  const queryClient = useQueryClient()
  const [outcome, setOutcome] = useState(initialOutcome ?? '')
  const [justification, setJustification] = useState(initialJustification ?? '')
  const [expiryDate, setExpiryDate] = useState(toDateInputValue(initialExpiryDate))
  const [expiryMode, setExpiryMode] = useState<'forever' | 'date'>(initialExpiryDate ? 'date' : 'forever')
  const [reEvaluationDate, setReEvaluationDate] = useState(toDateInputValue(initialReEvaluationDate))
  const [deferralMode, setDeferralMode] = useState<'forever' | 'date'>(initialReEvaluationDate ? 'date' : 'forever')
  const [submitting, setSubmitting] = useState(false)
  const [submitError, setSubmitError] = useState<string | null>(null)

  const needsJustification = REQUIRES_JUSTIFICATION.has(outcome)
  const needsExpiry = REQUIRES_EXPIRY.has(outcome)
  const needsReEvaluation = REQUIRES_REEVALUATION.has(outcome)

  const canSubmit =
    outcome !== '' &&
    (!needsJustification || justification.trim().length > 0) &&
    (!needsExpiry || expiryMode === 'forever' || expiryDate !== '') &&
    (!needsReEvaluation || deferralMode === 'forever' || reEvaluationDate !== '')

  async function handleSubmit() {
    if (!canSubmit) return
    setSubmitting(true)
    setSubmitError(null)
    try {
      await createDecision({
        data: {
          caseId,
          outcome,
          justification: justification || undefined,
          maintenanceWindowDate: undefined,
          expiryDate: needsExpiry && expiryMode === 'date' ? toIsoDateBoundary(expiryDate) : undefined,
          reEvaluationDate: needsReEvaluation && deferralMode === 'date' ? toIsoDateBoundary(reEvaluationDate) : undefined,
          deadlineMode: needsExpiry ? expiryMode : needsReEvaluation ? deferralMode : undefined,
        },
      })
      await queryClient.invalidateQueries({ queryKey })
      setOutcome('')
      setJustification('')
      setExpiryDate('')
      setExpiryMode('forever')
      setReEvaluationDate('')
      setDeferralMode('forever')
    } catch (error) {
      setSubmitError(getApiErrorMessage(error, 'Unable to save the remediation decision.'))
    } finally {
      setSubmitting(false)
    }
  }

  function handleOutcomeChange(value: string | null) {
    const nextOutcome = value ?? ''
    setOutcome(nextOutcome)

    if (!REQUIRES_EXPIRY.has(nextOutcome)) {
      setExpiryMode('forever')
      setExpiryDate('')
    }

    if (!REQUIRES_REEVALUATION.has(nextOutcome)) {
      setDeferralMode('forever')
      setReEvaluationDate('')
    }
  }

  return (
    <div className="space-y-5 pt-1">
      <div className="space-y-2">
        <label className="text-sm font-medium">Decision posture</label>
        <Select value={outcome} onValueChange={handleOutcomeChange} disabled={readOnly}>
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

      <div className="space-y-2">
        <label className="text-sm font-medium">
          Decision rationale
          {needsJustification ? <span className="text-tone-danger-foreground"> *</span> : null}
        </label>
        <Textarea
          value={justification}
          onChange={(e) => setJustification(e.target.value)}
          placeholder="Explain why this is the right posture for the affected assets..."
          rows={3}
          disabled={readOnly}
        />
        {!needsJustification ? (
          <p className="text-xs text-muted-foreground">
            Optional, but shown later to execution teams and approvers.
          </p>
        ) : null}
      </div>

      {needsExpiry ? (
        <div className="space-y-2">
          <label className="text-sm font-medium">Exception expiry</label>
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              variant={expiryMode === 'forever' ? 'default' : 'outline'}
              size="sm"
              disabled={readOnly}
              onClick={() => {
                setExpiryMode('forever')
                setExpiryDate('')
              }}
            >
              Forever / no fixed deadline
            </Button>
            <Button
              type="button"
              variant={expiryMode === 'date' ? 'default' : 'outline'}
              size="sm"
              disabled={readOnly}
              onClick={() => setExpiryMode('date')}
            >
              Expires on date
            </Button>
          </div>
          {expiryMode === 'date' ? (
            <Input
              type="date"
              value={expiryDate}
              onChange={(e) => setExpiryDate(e.target.value)}
              disabled={readOnly}
            />
          ) : null}
          <p className="text-xs text-muted-foreground">
            Risk acceptance and alternate mitigation can be permanent or time-bound.
          </p>
        </div>
      ) : null}

      {needsReEvaluation ? (
        <div className="space-y-2">
          <label className="text-sm font-medium">Deferral review</label>
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              variant={deferralMode === 'forever' ? 'default' : 'outline'}
              size="sm"
              disabled={readOnly}
              onClick={() => {
                setDeferralMode('forever')
                setReEvaluationDate('')
              }}
            >
              Forever / no fixed deadline
            </Button>
            <Button
              type="button"
              variant={deferralMode === 'date' ? 'default' : 'outline'}
              size="sm"
              disabled={readOnly}
              onClick={() => setDeferralMode('date')}
            >
              Review on date
            </Button>
          </div>
          {deferralMode === 'date' ? (
            <Input
              type="date"
              value={reEvaluationDate}
              onChange={(e) => setReEvaluationDate(e.target.value)}
              disabled={readOnly}
            />
          ) : null}
          <p className="text-xs text-muted-foreground">
            Choose no fixed deadline or set the date this deferred patch decision must be revisited.
          </p>
        </div>
      ) : null}

      {submitError ? (
        <p
          role="alert"
          className="rounded-md border border-tone-danger/40 bg-tone-danger/5 px-3 py-2 text-sm text-tone-danger-foreground"
        >
          {submitError}
        </p>
      ) : null}

      <div className="pt-1">
        <Button
          onClick={handleSubmit}
          disabled={readOnly || !canSubmit || submitting}
          className="w-full sm:w-auto"
        >
          {submitting ? 'Saving decision...' : submitLabel}
        </Button>
      </div>
    </div>
  )
}

function toIsoDateBoundary(value: string) {
  if (!value) {
    return undefined
  }

  return `${value}T00:00:00Z`
}

function toDateInputValue(value?: string | null) {
  return value ? value.slice(0, 10) : ''
}
