import { useState } from 'react'

type CreateTeamDialogProps = {
  isSubmitting: boolean
  onCreate: (payload: { name: string; tenantId: string }) => void
}

export function CreateTeamDialog({ isSubmitting, onCreate }: CreateTeamDialogProps) {
  const [name, setName] = useState('')
  const [tenantId, setTenantId] = useState('')

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <h3 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">Create Team</h3>
      <div className="mt-2 grid gap-2 md:grid-cols-[1fr_1fr_auto]">
        <input className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" placeholder="Team name" value={name} onChange={(event) => setName(event.target.value)} />
        <input className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" placeholder="Tenant GUID" value={tenantId} onChange={(event) => setTenantId(event.target.value)} />
        <button
          type="button"
          className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90 disabled:opacity-50"
          disabled={isSubmitting || name.trim().length === 0 || tenantId.trim().length === 0}
          onClick={() => {
            onCreate({ name: name.trim(), tenantId: tenantId.trim() })
            setName('')
            setTenantId('')
          }}
        >
          {isSubmitting ? 'Creating...' : 'Create'}
        </button>
      </div>
    </section>
  )
}
