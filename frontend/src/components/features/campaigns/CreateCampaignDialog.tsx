import { useState } from 'react'

type CreateCampaignDialogProps = {
  isSubmitting: boolean
  onCreate: (name: string, description?: string) => void
}

export function CreateCampaignDialog({ isSubmitting, onCreate }: CreateCampaignDialogProps) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <h3 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">Create Campaign</h3>
      <div className="mt-2 grid gap-2 md:grid-cols-[1fr_2fr_auto]">
        <input
          className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
          placeholder="Name"
          value={name}
          onChange={(event) => {
            setName(event.target.value)
          }}
        />
        <input
          className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
          placeholder="Description"
          value={description}
          onChange={(event) => {
            setDescription(event.target.value)
          }}
        />
        <button
          type="button"
          className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90 disabled:opacity-50"
          disabled={isSubmitting || name.trim().length === 0}
          onClick={() => {
            onCreate(name.trim(), description.trim() || undefined)
            setName('')
            setDescription('')
          }}
        >
          {isSubmitting ? 'Creating...' : 'Create'}
        </button>
      </div>
    </section>
  )
}
