import { useMemo, useState } from 'react'

type TaskStatusUpdateProps = {
  currentStatus: string
  isSubmitting: boolean
  onSubmit: (status: string, justification?: string) => void
}

const statusOptions = [
  'Pending',
  'InProgress',
  'PatchScheduled',
  'CannotPatch',
  'Completed',
  'RiskAccepted',
]

function requiresJustification(status: string): boolean {
  return status === 'CannotPatch' || status === 'RiskAccepted'
}

export function TaskStatusUpdate({ currentStatus, isSubmitting, onSubmit }: TaskStatusUpdateProps) {
  const [status, setStatus] = useState(currentStatus)
  const [justification, setJustification] = useState('')
  const shouldRequireJustification = useMemo(() => requiresJustification(status), [status])

  return (
    <div className="space-y-2 rounded-md border border-border/70 bg-muted/30 p-3">
      <label className="block space-y-1 text-xs text-muted-foreground">
        <span>Status</span>
        <select
          className="w-full rounded-md border border-input bg-background px-2 py-1.5 text-sm text-foreground"
          value={status}
          onChange={(event) => {
            setStatus(event.target.value)
          }}
        >
          {statusOptions.map((value) => (
            <option key={value} value={value}>
              {value}
            </option>
          ))}
        </select>
      </label>

      {shouldRequireJustification ? (
        <label className="block space-y-1 text-xs text-muted-foreground">
          <span>Justification</span>
          <textarea
            className="min-h-16 w-full rounded-md border border-input bg-background px-2 py-1.5 text-sm text-foreground"
            value={justification}
            onChange={(event) => {
              setJustification(event.target.value)
            }}
          />
        </label>
      ) : null}

      <button
        type="button"
        className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90 disabled:opacity-50"
        disabled={isSubmitting || (shouldRequireJustification && justification.trim().length === 0)}
        onClick={() => {
          onSubmit(status, shouldRequireJustification ? justification.trim() : undefined)
        }}
      >
        {isSubmitting ? 'Updating...' : 'Update status'}
      </button>
    </div>
  )
}
