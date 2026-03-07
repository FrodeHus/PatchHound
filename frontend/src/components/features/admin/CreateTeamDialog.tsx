import { useState } from 'react'

type CreateTeamDialogProps = {
  isSubmitting: boolean
  tenants: Array<{ id: string; name: string }>
  onCreate: (payload: { name: string; tenantId: string }) => void
}

export function CreateTeamDialog({ isSubmitting, tenants, onCreate }: CreateTeamDialogProps) {
  const [name, setName] = useState('')
  const [tenantId, setTenantId] = useState('')

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <h3 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">Create Assignment Group</h3>
      <p className="mt-1 text-sm text-muted-foreground">
        Create a reusable ownership group for a tenant. Users can be added to it afterwards.
      </p>
      <div className="mt-2 grid gap-2 md:grid-cols-[1fr_1fr_auto]">
        <input className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" placeholder="Assignment group name" value={name} onChange={(event) => setName(event.target.value)} />
        <select
          className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
          value={tenantId}
          onChange={(event) => setTenantId(event.target.value)}
        >
          <option value="">Select tenant</option>
          {tenants.map((tenant) => (
            <option key={tenant.id} value={tenant.id}>
              {tenant.name}
            </option>
          ))}
        </select>
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
          {isSubmitting ? 'Creating...' : 'Create Group'}
        </button>
      </div>
    </section>
  )
}
