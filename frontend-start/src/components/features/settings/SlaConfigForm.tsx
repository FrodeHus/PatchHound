import { useState } from 'react'

type SlaConfigFormProps = {
  onApply: (criticalDays: number, highDays: number, mediumDays: number, lowDays: number) => void
}

export function SlaConfigForm({ onApply }: SlaConfigFormProps) {
  const [critical, setCritical] = useState('7')
  const [high, setHigh] = useState('14')
  const [medium, setMedium] = useState('30')
  const [low, setLow] = useState('60')

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <h3 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">SLA Configuration</h3>
      <div className="mt-2 grid gap-2 md:grid-cols-4">
        <input className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" value={critical} onChange={(event) => setCritical(event.target.value)} placeholder="Critical days" />
        <input className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" value={high} onChange={(event) => setHigh(event.target.value)} placeholder="High days" />
        <input className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" value={medium} onChange={(event) => setMedium(event.target.value)} placeholder="Medium days" />
        <input className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" value={low} onChange={(event) => setLow(event.target.value)} placeholder="Low days" />
      </div>
      <button
        type="button"
        className="mt-2 rounded-md border border-input px-3 py-1.5 text-sm hover:bg-muted"
        onClick={() => {
          onApply(Number(critical), Number(high), Number(medium), Number(low))
        }}
      >
        Apply SLA values
      </button>
    </section>
  )
}
