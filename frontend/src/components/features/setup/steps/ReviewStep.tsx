import type { SetupPayload } from '@/api/useSetup'

type ReviewStepProps = {
  payload: SetupPayload
}

export function ReviewStep({ payload }: ReviewStepProps) {
  return (
    <section className="space-y-2 rounded-lg border border-border bg-card p-4">
      <h2 className="text-lg font-semibold">Review & Complete</h2>
      <dl className="grid gap-2 text-sm">
        <div><dt className="text-xs text-muted-foreground">Tenant Name</dt><dd>{payload.tenantName}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Entra Tenant ID</dt><dd>{payload.entraTenantId}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Admin Email</dt><dd>{payload.adminEmail}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Admin Display Name</dt><dd>{payload.adminDisplayName}</dd></div>
      </dl>
    </section>
  )
}
