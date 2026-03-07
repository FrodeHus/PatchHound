type TenantConfigStepProps = {
  tenantName: string
  entraTenantId: string
  onTenantNameChange: (value: string) => void
  onEntraTenantIdChange: (value: string) => void
}

export function TenantConfigStep({
  tenantName,
  entraTenantId,
  onTenantNameChange,
  onEntraTenantIdChange,
}: TenantConfigStepProps) {
  return (
    <section className="space-y-2 rounded-lg border border-border bg-card p-4">
      <h2 className="text-lg font-semibold">Tenant Configuration</h2>
      <input
        className="w-full rounded-md border border-input bg-background px-2 py-1.5 text-sm"
        placeholder="Tenant name"
        value={tenantName}
        onChange={(event) => {
          onTenantNameChange(event.target.value)
        }}
      />
      <input
        className="w-full rounded-md border border-input bg-background px-2 py-1.5 text-sm"
        placeholder="Entra Tenant ID"
        value={entraTenantId}
        onChange={(event) => {
          onEntraTenantIdChange(event.target.value)
        }}
      />
    </section>
  )
}
