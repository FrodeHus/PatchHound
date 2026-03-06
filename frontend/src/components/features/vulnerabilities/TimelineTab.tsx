import type { AuditLogItem } from '@/api/useVulnerabilities'

type TimelineTabProps = {
  items: AuditLogItem[]
}

export function TimelineTab({ items }: TimelineTabProps) {
  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <h3 className="mb-3 text-lg font-semibold">Timeline</h3>
      <ol className="space-y-3">
        {items.length === 0 ? <li className="text-sm text-muted-foreground">No audit events found.</li> : null}
        {items.map((item) => (
          <li key={item.id} className="rounded-md border border-border/70 p-3">
            <p className="text-sm font-medium">{item.action}</p>
            <p className="text-xs text-muted-foreground">{new Date(item.timestamp).toLocaleString()}</p>
          </li>
        ))}
      </ol>
    </section>
  )
}
