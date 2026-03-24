import type { AuditLogItem } from '@/api/audit-log.schemas'
import {
  AuditTimeline,
  type AuditTimelineEvent,
} from '@/components/features/audit/AuditTimeline'
import { InsetPanel } from '@/components/ui/inset-panel'
import { formatAuditEntityType, formatAuditKey, parseAuditValues } from '@/lib/audit'
import { formatDateTime } from '@/lib/formatting'

type TimelineTabProps = {
  items: AuditLogItem[]
}

export function TimelineTab({ items }: TimelineTabProps) {
  const summary = {
    total: items.length,
    latest: items[0]?.timestamp ?? null,
  }

  const events: AuditTimelineEvent[] = items.map((item) => ({
    id: item.id,
    action: item.action,
    title: summarizeHeadline(item),
    description: summarizeEntry(item),
    timestamp: item.timestamp,
    badges: item.entityLabel
      ? [{ label: item.entityLabel, tone: 'neutral' }]
      : undefined,
  }))

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <div className="space-y-1">
          <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
            Audit timeline
          </p>
          <h3 className="text-lg font-semibold">Recorded activity</h3>
        </div>
        <div className="flex flex-wrap gap-3">
          <SummaryMetric label="Events" value={String(summary.total)} />
          <SummaryMetric
            label="Latest"
            value={summary.latest ? formatDateTime(summary.latest) : 'None'}
          />
        </div>
      </div>

      <AuditTimeline events={events} emptyMessage="No audit events found." />
    </section>
  )
}

function summarizeHeadline(item: AuditLogItem) {
  const actor = item.userDisplayName ?? 'Unknown actor'
  const subject = item.entityLabel
    ? `${formatAuditEntityType(item.entityType)} ${item.entityLabel}`
    : formatAuditEntityType(item.entityType)

  switch (item.action) {
    case 'Created':
      return `${actor} created ${subject}.`
    case 'Deleted':
      return `${actor} deleted ${subject}.`
    default:
      return `${actor} updated ${subject}.`
  }
}

function summarizeEntry(item: AuditLogItem) {
  const newValues = parseAuditValues(item.newValues)
  const oldValues = parseAuditValues(item.oldValues)
  const changedKeys = [...new Set([...Object.keys(newValues), ...Object.keys(oldValues)])]

  if (!changedKeys.length || item.action !== 'Updated') {
    return undefined
  }

  const preview = changedKeys.slice(0, 3).map(formatAuditKey).join(', ')
  const suffix = changedKeys.length > 3 ? ` +${changedKeys.length - 3} more` : ''
  return `Changed fields: ${preview}${suffix}.`
}

function SummaryMetric({ label, value }: { label: string; value: string }) {
  return (
    <InsetPanel className="min-w-[120px] px-3 py-3">
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
        {label}
      </p>
      <p className="mt-1 text-sm font-medium text-foreground">{value}</p>
    </InsetPanel>
  )
}
