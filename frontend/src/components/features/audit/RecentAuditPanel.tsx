import { Link } from '@tanstack/react-router'
import type { AuditLogItem } from '@/api/audit-log.schemas'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { InsetPanel } from '@/components/ui/inset-panel'
import { formatAuditEntityType, formatAuditKey, parseAuditValues } from '@/lib/audit'
import { formatDateTime } from '@/lib/formatting'

type RecentAuditPanelProps = {
  title?: string
  description: string
  items: AuditLogItem[]
  emptyMessage: string
}

export function RecentAuditPanel({
  title = 'Recent Activity',
  description,
  items,
  emptyMessage,
}: RecentAuditPanelProps) {
  return (
    <Card className="rounded-3xl">
      <CardHeader className="flex flex-row items-start justify-between gap-4">
        <div className="space-y-1">
          <CardTitle>{title}</CardTitle>
          <p className="text-sm text-muted-foreground">{description}</p>
        </div>
        <Link
          to="/audit-log"
          search={{ page: 1, pageSize: 25, action: '', entityType: '' }}
          className="text-sm font-medium text-primary hover:underline"
        >
          Open audit trail
        </Link>
      </CardHeader>
      <CardContent className="space-y-3">
        {items.length === 0 ? (
          <InsetPanel className="px-4 py-6 text-sm text-muted-foreground">
            {emptyMessage}
          </InsetPanel>
        ) : (
          items.map((item) => (
            <InsetPanel key={item.id} className="px-4 py-3">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="space-y-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge className={actionBadgeClassName(item.action)}>{item.action}</Badge>
                    <span className="text-sm font-medium text-foreground">{formatAuditEntityType(item.entityType)}</span>
                    {item.entityLabel ? (
                      <Badge variant="outline" className="rounded-full border-border/80 bg-card text-foreground">
                        {item.entityLabel}
                      </Badge>
                    ) : null}
                  </div>
                  <p className="text-sm text-muted-foreground">{summarizeEntry(item)}</p>
                </div>
                <span className="text-xs text-muted-foreground">{formatTimestamp(item.timestamp)}</span>
              </div>
            </InsetPanel>
          ))
        )}
      </CardContent>
    </Card>
  )
}

function summarizeEntry(item: AuditLogItem) {
  const newValues = parseAuditValues(item.newValues)
  const oldValues = parseAuditValues(item.oldValues)
  const changedKeys = [...new Set([...Object.keys(newValues), ...Object.keys(oldValues)])]
  const actor = item.userDisplayName ?? 'Unknown operator'
  const subject = item.entityLabel
    ? `${formatAuditEntityType(item.entityType)} ${item.entityLabel}`
    : formatAuditEntityType(item.entityType)

  if (item.action === 'Created') {
    return `${actor} created ${subject}.`
  }

  if (item.action === 'Deleted') {
    return `${actor} deleted ${subject}.`
  }

  if (!changedKeys.length) {
    return `${actor} updated ${subject}.`
  }

  const preview = changedKeys.slice(0, 3).map(formatAuditKey).join(', ')
  const suffix = changedKeys.length > 3 ? ` +${changedKeys.length - 3} more` : ''
  return `${actor} updated ${subject}: ${preview}${suffix}.`
}

function formatTimestamp(value: string) {
  return formatDateTime(value)
}

function actionBadgeClassName(action: string) {
  switch (action) {
    case 'Created':
      return 'rounded-full border border-tone-success-border bg-tone-success text-tone-success-foreground hover:bg-tone-success'
    case 'Updated':
      return 'rounded-full border border-primary/20 bg-primary/10 text-primary hover:bg-primary/10'
    case 'Deleted':
      return 'rounded-full border border-destructive/25 bg-destructive/10 text-destructive hover:bg-destructive/10'
    default:
      return 'rounded-full border border-border/80 bg-card text-foreground hover:bg-card'
  }
}
