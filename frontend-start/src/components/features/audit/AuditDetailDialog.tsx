import type { AuditLogItem } from '@/api/useAuditLog'

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
      <div className="max-h-[80vh] w-full max-w-3xl overflow-auto rounded-lg border border-border bg-background p-4 shadow-lg">
        <div className="mb-3 flex items-center justify-between">
          <h3 className="text-lg font-semibold">Audit Entry Detail</h3>
          <button type="button" className="rounded-md border border-input px-2 py-1 text-sm" onClick={onClose}>
            Close
          </button>
        </div>

        <dl className="mb-3 grid gap-2 text-sm md:grid-cols-2">
          <div><dt className="text-xs text-muted-foreground">Entity Type</dt><dd>{selected.entityType}</dd></div>
          <div><dt className="text-xs text-muted-foreground">Action</dt><dd>{selected.action}</dd></div>
          <div><dt className="text-xs text-muted-foreground">Entity ID</dt><dd><code>{selected.entityId}</code></dd></div>
          <div><dt className="text-xs text-muted-foreground">Timestamp</dt><dd>{new Date(selected.timestamp).toLocaleString()}</dd></div>
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
