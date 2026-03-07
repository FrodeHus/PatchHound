import { useState } from 'react'

type InviteUserDialogProps = {
  isSubmitting: boolean
  onInvite: (payload: { email: string; displayName: string; entraObjectId: string }) => void
}

export function InviteUserDialog({ isSubmitting, onInvite }: InviteUserDialogProps) {
  const [email, setEmail] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [entraObjectId, setEntraObjectId] = useState('')

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <h3 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">Invite User</h3>
      <div className="mt-2 grid gap-2 md:grid-cols-4">
        <input className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" placeholder="Email" value={email} onChange={(event) => setEmail(event.target.value)} />
        <input className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" placeholder="Display name" value={displayName} onChange={(event) => setDisplayName(event.target.value)} />
        <input className="rounded-md border border-input bg-background px-2 py-1.5 text-sm" placeholder="Entra Object ID" value={entraObjectId} onChange={(event) => setEntraObjectId(event.target.value)} />
        <button
          type="button"
          className="rounded-md bg-primary px-3 py-1.5 text-sm text-primary-foreground hover:opacity-90 disabled:opacity-50"
          disabled={isSubmitting || email.trim().length === 0 || displayName.trim().length === 0 || entraObjectId.trim().length === 0}
          onClick={() => {
            onInvite({ email: email.trim(), displayName: displayName.trim(), entraObjectId: entraObjectId.trim() })
            setEmail('')
            setDisplayName('')
            setEntraObjectId('')
          }}
        >
          {isSubmitting ? 'Inviting...' : 'Invite'}
        </button>
      </div>
    </section>
  )
}
