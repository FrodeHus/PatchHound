import { useState } from 'react'

type SlaConfigFormProps = {
  onApply: (criticalDays: number, highDays: number, mediumDays: number, lowDays: number) => void
}

function isValidDays(value: string): boolean {
  const n = Number(value)
  return Number.isInteger(n) && n > 0
}

export function SlaConfigForm({ onApply }: SlaConfigFormProps) {
  const [critical, setCritical] = useState('7')
  const [high, setHigh] = useState('14')
  const [medium, setMedium] = useState('30')
  const [low, setLow] = useState('60')

  const allValid = isValidDays(critical) && isValidDays(high) && isValidDays(medium) && isValidDays(low)

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <h3 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">SLA Configuration</h3>
      <div className="mt-2 grid gap-2 md:grid-cols-4">
        <input type="number" min="1" step="1" className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" value={critical} onChange={(event) => setCritical(event.target.value)} placeholder="Critical days" />
        <input type="number" min="1" step="1" className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" value={high} onChange={(event) => setHigh(event.target.value)} placeholder="High days" />
        <input type="number" min="1" step="1" className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" value={medium} onChange={(event) => setMedium(event.target.value)} placeholder="Medium days" />
        <input type="number" min="1" step="1" className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" value={low} onChange={(event) => setLow(event.target.value)} placeholder="Low days" />
      </div>
      <button
        type="button"
        className="mt-2 rounded-md border border-input px-3 py-1.5 text-sm hover:bg-muted disabled:opacity-50 disabled:cursor-not-allowed"
        disabled={!allValid}
        onClick={() => {
          if (!allValid) return
          onApply(Number(critical), Number(high), Number(medium), Number(low))
        }}
      >
        Apply SLA values
      </button>
    </section>
  )
}
