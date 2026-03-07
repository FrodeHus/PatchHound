import type { SetupContext } from '@/api/setup.schemas'

type AdminUserStepProps = {
  setupContext: SetupContext
}

export function AdminUserStep({ setupContext }: AdminUserStepProps) {
  return (
    <section className="space-y-2 rounded-lg border border-border bg-card p-4">
      <h2 className="text-lg font-semibold">Admin User</h2>
      <p className="text-sm text-muted-foreground">
        The currently logged-in Entra user will be granted the first Global Admin role.
      </p>
      <dl className="grid gap-2 text-sm">
        <div><dt className="text-xs text-muted-foreground">Email</dt><dd>{setupContext.adminEmail}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Display Name</dt><dd>{setupContext.adminDisplayName}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Entra Object ID</dt><dd>{setupContext.adminEntraObjectId}</dd></div>
      </dl>
    </section>
  )
}
