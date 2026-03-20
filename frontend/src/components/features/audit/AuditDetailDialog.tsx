import type { AuditLogItem } from '@/api/audit-log.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { InsetPanel } from '@/components/ui/inset-panel'

type AuditDetailDialogProps = {
  selected: AuditLogItem | null
  onClose: () => void
}

export function AuditDetailDialog({ selected, onClose }: AuditDetailDialogProps) {
  if (!selected) {
    return null
  }

  return (
    <Dialog open onOpenChange={(open) => {
      if (!open) {
        onClose()
      }
    }}>
      <DialogContent className="w-[min(96vw,72rem)] max-w-[72rem] overflow-hidden rounded-2xl border-border/80 bg-card p-0 sm:max-w-[72rem]">
        <DialogHeader className="border-b border-border/60 px-5 py-4">
          <DialogTitle>Audit Entry Detail</DialogTitle>
        </DialogHeader>
        <div className="max-h-[76vh] overflow-auto px-5 py-4">
        <dl className="mb-4 grid gap-3 text-sm md:grid-cols-2 xl:grid-cols-3">
          <InsetPanel className="p-3"><dt className="text-xs text-muted-foreground">Entity Type</dt><dd className="mt-2 font-medium">{selected.entityType}</dd></InsetPanel>
          <InsetPanel className="p-3"><dt className="text-xs text-muted-foreground">Entity Label</dt><dd className="mt-2 font-medium">{selected.entityLabel ?? '(none)'}</dd></InsetPanel>
          <InsetPanel className="p-3"><dt className="text-xs text-muted-foreground">Action</dt><dd className="mt-2"><Badge>{selected.action}</Badge></dd></InsetPanel>
          <InsetPanel className="p-3"><dt className="text-xs text-muted-foreground">Actor</dt><dd className="mt-2 font-medium">{selected.userDisplayName ?? 'Unknown operator'}</dd></InsetPanel>
          <InsetPanel className="p-3"><dt className="text-xs text-muted-foreground">Entity ID</dt><dd className="mt-2 break-all"><code>{selected.entityId}</code></dd></InsetPanel>
          <InsetPanel className="p-3"><dt className="text-xs text-muted-foreground">Timestamp</dt><dd className="mt-2">{new Date(selected.timestamp).toLocaleString()}</dd></InsetPanel>
        </dl>

        <div className="grid gap-3 md:grid-cols-2">
          <section>
            <h4 className="mb-1 text-sm font-medium">Old Values</h4>
            <pre className="max-h-80 overflow-auto rounded-md border border-border/80 bg-muted/60 p-2 text-xs">{selected.oldValues ?? '(none)'}</pre>
          </section>
          <section>
            <h4 className="mb-1 text-sm font-medium">New Values</h4>
            <pre className="max-h-80 overflow-auto rounded-md border border-border/80 bg-muted/60 p-2 text-xs">{selected.newValues ?? '(none)'}</pre>
          </section>
        </div>
        </div>
        <DialogFooter className="border-t border-border/60 bg-card px-5 py-4">
          <Button type="button" variant="outline" onClick={onClose}>
            Close
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
