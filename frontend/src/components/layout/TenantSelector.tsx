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
  if (tenants.length <= 1) {
    return null
  }

  return (
    <div className="flex items-center gap-2">
      <div className="hidden h-8 items-center gap-2 rounded-full border border-border/70 bg-card/70 px-3 text-xs text-muted-foreground lg:flex">
        <Building2 className="size-3.5 text-primary" />
        Scope
      </div>
      <Select
        value={selectedTenantId ?? tenants[0]?.id ?? ''}
        onValueChange={onSelectTenant}
      >
        <SelectTrigger className="h-10 min-w-44 rounded-full border-border/70 bg-card/80 px-4 backdrop-blur">
          <SelectValue placeholder="Select tenant" />
        </SelectTrigger>
        <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
          {tenants.map((tenant) => (
            <SelectItem key={tenant.id} value={tenant.id}>
              {tenant.name}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  )
}
