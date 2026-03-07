import type { TenantAccess } from '@/types/api'

type TenantSelectorProps = {
  tenants: TenantAccess[]
  selectedTenantId: string | null
  onSelectTenant: (tenantId: string) => void
}

export function TenantSelector({
  tenants,
  selectedTenantId,
  onSelectTenant,
}: TenantSelectorProps) {
  if (tenants.length <= 1) {
    return null
  }

  return (
    <label className="flex items-center gap-2 text-sm text-muted-foreground" htmlFor="tenant-selector">
      <span>Tenant</span>
      <select
        id="tenant-selector"
        className="rounded-md border border-input bg-background px-2 py-1 text-foreground"
        value={selectedTenantId ?? tenants[0]?.id ?? ''}
        onChange={(event) => {
          onSelectTenant(event.target.value)
        }}
      >
        {tenants.map((tenant) => (
          <option key={tenant.id} value={tenant.id}>
            {tenant.name}
          </option>
        ))}
      </select>
    </label>
  )
}
