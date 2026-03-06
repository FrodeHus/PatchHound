import { useState } from 'react'

type AiProviderFormProps = {
  onApply: (provider: string, model: string) => void
}

export function AiProviderForm({ onApply }: AiProviderFormProps) {
  const [provider, setProvider] = useState('mock')
  const [model, setModel] = useState('default')

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <h3 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">AI Provider</h3>
      <div className="mt-2 grid gap-2 md:grid-cols-2">
        <input className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" value={provider} onChange={(event) => setProvider(event.target.value)} placeholder="Provider" />
        <input className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" value={model} onChange={(event) => setModel(event.target.value)} placeholder="Model" />
      </div>
      <button
        type="button"
        className="mt-2 rounded-md border border-input px-3 py-1.5 text-sm hover:bg-muted"
        onClick={() => {
          onApply(provider.trim(), model.trim())
        }}
      >
        Apply AI provider
      </button>
    </section>
  )
}
