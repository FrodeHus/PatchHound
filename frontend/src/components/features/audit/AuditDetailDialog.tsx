import type { AuditLogItem } from '@/api/audit-log.schemas'
import { Badge } from '@/components/ui/badge'

type AuditDetailDialogProps = {
  selected: AuditLogItem | null
  onClose: () => void
}

export function AuditDetailDialog({ selected, onClose }: AuditDetailDialogProps) {
  if (!selected) {
    return null
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 p-4">
      <div className="max-h-[80vh] w-full max-w-4xl overflow-auto rounded-[28px] border border-border bg-background p-5 shadow-lg">
        <div className="mb-3 flex items-center justify-between">
          <h3 className="text-lg font-semibold">Audit Entry Detail</h3>
          <button type="button" className="rounded-full border border-input px-3 py-1.5 text-sm" onClick={onClose}>
            Close
          </button>
        </div>

        <dl className="mb-4 grid gap-3 text-sm md:grid-cols-2 xl:grid-cols-3">
          <div className="rounded-2xl border border-border/70 bg-background/25 p-3"><dt className="text-xs text-muted-foreground">Entity Type</dt><dd className="mt-2 font-medium">{selected.entityType}</dd></div>
          <div className="rounded-2xl border border-border/70 bg-background/25 p-3"><dt className="text-xs text-muted-foreground">Entity Label</dt><dd className="mt-2 font-medium">{selected.entityLabel ?? '(none)'}</dd></div>
          <div className="rounded-2xl border border-border/70 bg-background/25 p-3"><dt className="text-xs text-muted-foreground">Action</dt><dd className="mt-2"><Badge>{selected.action}</Badge></dd></div>
          <div className="rounded-2xl border border-border/70 bg-background/25 p-3"><dt className="text-xs text-muted-foreground">Actor</dt><dd className="mt-2 font-medium">{selected.userDisplayName ?? 'Unknown operator'}</dd></div>
          <div className="rounded-2xl border border-border/70 bg-background/25 p-3"><dt className="text-xs text-muted-foreground">Entity ID</dt><dd className="mt-2 break-all"><code>{selected.entityId}</code></dd></div>
          <div className="rounded-2xl border border-border/70 bg-background/25 p-3"><dt className="text-xs text-muted-foreground">Timestamp</dt><dd className="mt-2">{new Date(selected.timestamp).toLocaleString()}</dd></div>
        </dl>

        <div className="grid gap-3 md:grid-cols-2">
          <section>
            <h4 className="mb-1 text-sm font-medium">Old Values</h4>
            <pre className="max-h-80 overflow-auto rounded-md border border-border/70 bg-muted/30 p-2 text-xs">{selected.oldValues ?? '(none)'}</pre>
          </section>
          <section>
            <h4 className="mb-1 text-sm font-medium">New Values</h4>
            <pre className="max-h-80 overflow-auto rounded-md border border-border/70 bg-muted/30 p-2 text-xs">{selected.newValues ?? '(none)'}</pre>
          </section>
        </div>
      </div>
    </div>
  )
}
