import { useEffect, useState } from 'react'
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
  workflowId?: string | null
  queryKey: readonly unknown[]
  readOnly?: boolean
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
  tenantSoftwareId,
  workflowId,
  queryKey,
  readOnly = false,
}: DecisionFormProps) {
  const queryClient = useQueryClient()
  const [outcome, setOutcome] = useState('')
  const [justification, setJustification] = useState('')
  const [expiryDate, setExpiryDate] = useState('')
  const [expiryMode, setExpiryMode] = useState<'permanent' | 'expires'>('permanent')
  const [reEvaluationDate, setReEvaluationDate] = useState('')
  const [submitting, setSubmitting] = useState(false)

  const needsJustification = REQUIRES_JUSTIFICATION.has(outcome)
  const needsExpiry = REQUIRES_EXPIRY.has(outcome)
  const needsReEvaluation = REQUIRES_REEVALUATION.has(outcome)

  useEffect(() => {
    if (!needsExpiry) {
      setExpiryMode('permanent')
      setExpiryDate('')
    }
  }, [needsExpiry])

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
          workflowId,
          outcome,
          justification: justification || undefined,
          expiryDate: needsExpiry && expiryMode === 'expires' ? toIsoDateBoundary(expiryDate) : undefined,
          reEvaluationDate: toIsoDateBoundary(reEvaluationDate),
        },
      })
      await queryClient.invalidateQueries({ queryKey })
      setOutcome('')
      setJustification('')
      setExpiryDate('')
      setExpiryMode('permanent')
      setReEvaluationDate('')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="space-y-4">
      {readOnly ? (
        <div className="rounded-xl border border-dashed border-border/70 bg-background/50 px-4 py-3 text-sm text-muted-foreground">
          This stage is visible in read-only mode. Only the current remediation decision owner can submit the next decision.
        </div>
      ) : null}
      <div className="space-y-2">
        <label className="text-sm font-medium">Outcome</label>
        <Select value={outcome} onValueChange={(v) => setOutcome(v ?? '')} disabled={readOnly}>
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
            disabled={readOnly}
          />
        </div>
      ) : null}

      {needsExpiry ? (
        <div className="space-y-2">
          <label className="text-sm font-medium">Expiry Date</label>
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              variant={expiryMode === 'permanent' ? 'default' : 'outline'}
              size="sm"
              disabled={readOnly}
              onClick={() => {
                setExpiryMode('permanent')
                setExpiryDate('')
              }}
            >
              Permanent or until cancelled
            </Button>
            <Button
              type="button"
              variant={expiryMode === 'expires' ? 'default' : 'outline'}
              size="sm"
              disabled={readOnly}
              onClick={() => setExpiryMode('expires')}
            >
              Expires on date
            </Button>
          </div>
          {expiryMode === 'expires' ? (
            <Input
              type="date"
              value={expiryDate}
              onChange={(e) => setExpiryDate(e.target.value)}
              disabled={readOnly}
            />
          ) : null}
          <p className="text-xs text-muted-foreground">
            Risk acceptance and alternate mitigation can be permanent, or set to expire on a specific date.
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
            disabled={readOnly}
          />
          <p className="text-xs text-muted-foreground">
            When this deferral should be reassessed.
          </p>
        </div>
      ) : null}

      <Button
        onClick={handleSubmit}
        disabled={readOnly || !canSubmit || submitting}
        className="w-full sm:w-auto"
      >
        {submitting ? 'Submitting...' : 'Submit Decision'}
      </Button>
    </div>
  )
}

function toIsoDateBoundary(value: string) {
  if (!value) {
    return undefined
  }

  return `${value}T00:00:00Z`
}
