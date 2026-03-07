import { useState } from 'react'

type FallbackChainFormProps = {
  onApply: (fallbackTeamId: string, defaultTeamId: string) => void
}

export function FallbackChainForm({ onApply }: FallbackChainFormProps) {
  const [fallbackTeamId, setFallbackTeamId] = useState('')
  const [defaultTeamId, setDefaultTeamId] = useState('')

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <h3 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">Fallback Chain</h3>
      <p className="mt-1 text-sm text-muted-foreground">
        Configure default assignment routing when an asset does not have a direct owner.
      </p>
      <div className="mt-2 grid gap-2 md:grid-cols-2">
        <input className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" value={fallbackTeamId} onChange={(event) => setFallbackTeamId(event.target.value)} placeholder="Fallback assignment group GUID" />
        <input className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" value={defaultTeamId} onChange={(event) => setDefaultTeamId(event.target.value)} placeholder="Default assignment group GUID" />
      </div>
      <button
        type="button"
        className="mt-2 rounded-md border border-input px-3 py-1.5 text-sm hover:bg-muted"
        onClick={() => {
          onApply(fallbackTeamId.trim(), defaultTeamId.trim())
        }}
      >
        Apply fallback chain
      </button>
    </section>
  )
}
