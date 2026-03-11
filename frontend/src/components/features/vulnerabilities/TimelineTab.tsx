import type { AuditLogItem } from '@/api/audit-log.schemas'
import { InsetPanel } from '@/components/ui/inset-panel'

type TimelineTabProps = {
  items: AuditLogItem[]
}

export function TimelineTab({ items }: TimelineTabProps) {
  const summary = {
    total: items.length,
    latest: items[0]?.timestamp ?? null,
  }

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <div className="space-y-1">
          <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Audit timeline</p>
          <h3 className="text-lg font-semibold">Recorded activity</h3>
        </div>
        <div className="flex flex-wrap gap-3">
          <SummaryMetric label="Events" value={String(summary.total)} />
          <SummaryMetric label="Latest" value={summary.latest ? new Date(summary.latest).toLocaleString() : 'None'} />
        </div>
      </div>

      <ol className="space-y-3">
        {items.length === 0 ? <InsetPanel as="li" className="px-4 py-4 text-sm text-muted-foreground">No audit events found.</InsetPanel> : null}
        {items.map((item) => (
          <InsetPanel key={item.id} as="li" className="px-4 py-4">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="space-y-2">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="rounded-full border border-sky-300/70 bg-sky-50 px-2.5 py-1 text-[11px] font-medium uppercase tracking-[0.14em] text-sky-900">
                    {item.action}
                  </span>
                  {item.entityLabel ? (
                    <span className="rounded-full border border-border/80 bg-card px-2.5 py-1 text-[11px] font-medium uppercase tracking-[0.12em] text-muted-foreground">
                      {item.entityLabel}
                    </span>
                  ) : null}
                </div>
                <p className="text-sm text-foreground">
                  {item.userDisplayName ?? 'Unknown actor'} changed {item.entityType}
                  {item.entityLabel ? ` ${item.entityLabel}` : ''}.
                </p>
              </div>
              <span className="text-xs text-muted-foreground">{new Date(item.timestamp).toLocaleString()}</span>
            </div>
          </InsetPanel>
        ))}
      </ol>
    </section>
  )
}

function SummaryMetric({ label, value }: { label: string; value: string }) {
  return (
    <InsetPanel className="min-w-[120px] px-3 py-3">
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
      <p className="mt-1 text-sm font-medium text-foreground">{value}</p>
    </InsetPanel>
  )
}
