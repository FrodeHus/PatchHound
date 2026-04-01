import { ChevronDown } from 'lucide-react'
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
    <div className="flex min-w-[200px] flex-col items-center justify-center text-center">
      <span className="text-[10px] font-medium uppercase tracking-[0.22em] text-muted-foreground/85">
        Current tenant
      </span>
      {isSingleTenant ? (
        <div className="mt-0.5 max-w-[240px] truncate text-xl font-semibold tracking-[-0.03em] text-foreground">
          {selectedTenantName}
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
          <SelectTrigger className="mt-0.5 h-auto min-w-0 border-0 bg-transparent px-0 py-0 text-center shadow-none hover:bg-transparent focus:ring-0 focus:ring-offset-0">
            <SelectValue>
              <span className="inline-flex items-center gap-1.5 text-xl font-semibold tracking-[-0.03em] text-foreground">
                <span className="max-w-[240px] truncate">{selectedTenantName}</span>
                <ChevronDown className="size-4 text-muted-foreground" />
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
