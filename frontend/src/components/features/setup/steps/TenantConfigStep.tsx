import type { SetupContext } from '@/api/setup.schemas'

type TenantConfigStepProps = {
  setupContext: SetupContext
}

export function TenantConfigStep({ setupContext }: TenantConfigStepProps) {
  return (
    <section className="space-y-2 rounded-lg border border-border bg-card p-4">
      <h2 className="text-lg font-semibold">Tenant Configuration</h2>
      <p className="text-sm text-muted-foreground">
        PatchHound detected your tenant from the authenticated Entra session.
      </p>
      <dl className="grid gap-2 text-sm">
        <div><dt className="text-xs text-muted-foreground">Tenant Name</dt><dd>{setupContext.tenantName}</dd></div>
        <div><dt className="text-xs text-muted-foreground">Entra Tenant ID</dt><dd>{setupContext.entraTenantId}</dd></div>
      </dl>
    </section>
  )
}
