import { Building2 } from 'lucide-react'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription } from '@/components/ui/dialog'
import { TenantSelector } from '@/components/layout/TenantSelector'

type TenantUnavailableDialogProps = {
  open: boolean
  tenants: Array<{ id: string; name: string }>
  onSelectTenant: (tenantId: string) => void
}

export function TenantUnavailableDialog({ open, tenants, onSelectTenant }: TenantUnavailableDialogProps) {
  return (
    <Dialog open={open} onOpenChange={() => { /* non-dismissable */ }} dismissible={false}>
      <DialogContent
        className="max-w-sm"
        showCloseButton={false}
      >
        <DialogHeader className="items-center text-center">
          <span className="flex size-12 items-center justify-center rounded-2xl border border-border/60 bg-muted/40 mb-2">
            <Building2 className="size-5 text-muted-foreground" />
          </span>
          <DialogTitle>Tenant no longer available</DialogTitle>
          <DialogDescription>
            This tenant is being deleted and can no longer be accessed. Please select another tenant to continue.
          </DialogDescription>
        </DialogHeader>
        <div className="mt-2 flex justify-center">
          <TenantSelector
            tenants={tenants}
            selectedTenantId={null}
            onSelectTenant={onSelectTenant}
          />
        </div>
      </DialogContent>
    </Dialog>
  )
}
