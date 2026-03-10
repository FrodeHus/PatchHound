import { Building2 } from 'lucide-react'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

type TenantSelectorProps = {
  tenants: Array<{ id: string; name: string }>
  selectedTenantId: string | null
  onSelectTenant: (tenantId: string) => void
}

export function TenantSelector({
  tenants,
  selectedTenantId,
  onSelectTenant,
}: TenantSelectorProps) {
  if (tenants.length === 0) {
    return null
  }

  const selectedTenantName = tenants.find((tenant) => tenant.id === selectedTenantId)?.name ?? 'Select tenant'
  const isSingleTenant = tenants.length === 1

  return (
    <div className="flex items-center gap-2 rounded-full border border-border/70 bg-card/72 px-2 py-2 backdrop-blur-xl">
      <div className="hidden items-center gap-2 rounded-full bg-background/55 px-3 py-2 text-[11px] uppercase tracking-[0.18em] text-muted-foreground lg:flex">
        <Building2 className="size-3.5 text-primary" />
        Operating scope
      </div>
      {isSingleTenant ? (
        <div className="flex h-11 min-w-52 flex-col justify-center rounded-full border border-border/70 bg-background/70 px-4 backdrop-blur">
          <span className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Tenant</span>
          <span className="truncate text-sm font-medium text-foreground">{selectedTenantName}</span>
        </div>
      ) : (
        <Select
          value={selectedTenantId ?? tenants[0]?.id ?? ''}
          onValueChange={(tenantId) => {
            if (tenantId) {
              onSelectTenant(tenantId)
            }
          }}
        >
          <SelectTrigger className="h-11 min-w-52 rounded-full border-border/70 bg-background/70 px-4 backdrop-blur">
            <SelectValue>
              <span className="flex min-w-0 flex-col">
                <span className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Tenant</span>
                <span className="truncate text-sm font-medium text-foreground">{selectedTenantName}</span>
              </span>
            </SelectValue>
          </SelectTrigger>
          <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
            {tenants.map((tenant) => (
              <SelectItem key={tenant.id} value={tenant.id}>
                {tenant.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      )}
    </div>
  )
}
