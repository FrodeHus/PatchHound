import { Link } from '@tanstack/react-router'
import type { AuditLogItem } from '@/api/audit-log.schemas'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

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
    <Card className="rounded-[28px] border-border/70 bg-card/82">
      <CardHeader className="flex flex-row items-start justify-between gap-4">
        <div className="space-y-1">
          <CardTitle>{title}</CardTitle>
          <p className="text-sm text-muted-foreground">{description}</p>
        </div>
        <Link to="/audit-log" className="text-sm font-medium text-primary hover:underline">
          Open audit trail
        </Link>
      </CardHeader>
      <CardContent className="space-y-3">
        {items.length === 0 ? (
          <div className="rounded-2xl border border-border/60 bg-background/25 px-4 py-6 text-sm text-muted-foreground">
            {emptyMessage}
          </div>
        ) : (
          items.map((item) => (
            <div key={item.id} className="rounded-2xl border border-border/60 bg-background/25 px-4 py-3">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="space-y-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge className={actionBadgeClassName(item.action)}>{item.action}</Badge>
                    <span className="text-sm font-medium text-foreground">{formatEntityType(item.entityType)}</span>
                  </div>
                  <p className="text-sm text-muted-foreground">{summarizeEntry(item)}</p>
                </div>
                <span className="text-xs text-muted-foreground">{formatTimestamp(item.timestamp)}</span>
              </div>
            </div>
          ))
        )}
      </CardContent>
    </Card>
  )
}

function summarizeEntry(item: AuditLogItem) {
  const newValues = parseValues(item.newValues)
  const oldValues = parseValues(item.oldValues)
  const changedKeys = [...new Set([...Object.keys(newValues), ...Object.keys(oldValues)])]

  if (!changedKeys.length) {
    return 'Change recorded without field details.'
  }

  const preview = changedKeys.slice(0, 3).map(formatKey).join(', ')
  const suffix = changedKeys.length > 3 ? ` +${changedKeys.length - 3} more` : ''
  return `Updated fields: ${preview}${suffix}`
}

function parseValues(raw: string | null) {
  if (!raw) {
    return {}
  }

  try {
    const parsed = JSON.parse(raw)
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, unknown>) : {}
  } catch {
    return {}
  }
}

function formatEntityType(value: string) {
  return value
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/Id\b/g, 'ID')
}

function formatKey(value: string) {
  return value
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/Id\b/g, 'ID')
    .toLowerCase()
}

function formatTimestamp(value: string) {
  return new Date(value).toLocaleString()
}

function actionBadgeClassName(action: string) {
  switch (action) {
    case 'Created':
      return 'rounded-full border border-emerald-400/25 bg-emerald-400/10 text-emerald-200 hover:bg-emerald-400/10'
    case 'Updated':
      return 'rounded-full border border-primary/20 bg-primary/10 text-primary hover:bg-primary/10'
    case 'Deleted':
      return 'rounded-full border border-destructive/25 bg-destructive/10 text-destructive hover:bg-destructive/10'
    default:
      return 'rounded-full border border-border/70 bg-background/70 text-foreground hover:bg-background/70'
  }
}
