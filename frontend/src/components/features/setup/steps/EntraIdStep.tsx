type EntraIdStepProps = {
  tenantSettings: string
  onTenantSettingsChange: (value: string) => void
}

export function EntraIdStep({ tenantSettings, onTenantSettingsChange }: EntraIdStepProps) {
  return (
    <section className="space-y-2 rounded-lg border border-border bg-card p-4">
      <h2 className="text-lg font-semibold">Entra ID Connection</h2>
      <p className="text-sm text-muted-foreground">
        Provide JSON settings for Entra registration and auth integration.
      </p>
      <textarea
        className="min-h-40 w-full rounded-md border border-input bg-background px-2 py-1.5 font-mono text-xs"
        value={tenantSettings}
        onChange={(event) => {
          onTenantSettingsChange(event.target.value)
        }}
      />
    </section>
  )
}
