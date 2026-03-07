import type { SetupContext } from '@/api/setup.schemas'

type ReviewStepProps = {
  setupContext: SetupContext
}

export function ReviewStep({ setupContext }: ReviewStepProps) {
  return (
    <section className="space-y-2 rounded-lg border border-border bg-card p-4">
      <h2 className="text-lg font-semibold">Review & Complete</h2>
      <dl className="grid gap-2 text-sm">
        <div><dt className="text-xs text-muted-foreground">Tenant Name</dt><dd>{setupContext.tenantName}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Entra Tenant ID</dt><dd>{setupContext.entraTenantId}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Admin Email</dt><dd>{setupContext.adminEmail}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Admin Display Name</dt><dd>{setupContext.adminDisplayName}</dd></div>
      </dl>
    </section>
  )
}
