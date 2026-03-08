import { useState } from 'react'
import type { AuditLogItem } from '@/api/audit-log.schemas'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { AuditDetailDialog } from '@/components/features/audit/AuditDetailDialog'
import { PaginationControls } from '@/components/ui/pagination-controls'

type AuditLogTableProps = {
  items: AuditLogItem[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
}

export function AuditLogTable({
  items,
  totalCount,
  page,
  pageSize,
  totalPages,
  onPageChange,
  onPageSizeChange,
}: AuditLogTableProps) {
  const [selected, setSelected] = useState<AuditLogItem | null>(null)

  return (
    <>
      <Card className="rounded-[28px] border-border/70 bg-card/82">
        <CardHeader>
          <div className="flex flex-wrap items-end justify-between gap-3">
            <div>
              <CardTitle>Audit Trail</CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">
                Review configuration and administrative changes with actor and entity context.
              </p>
            </div>
            <Badge variant="outline" className="rounded-full border-border/70 bg-background/60">
              {totalCount} total
            </Badge>
          </div>
        </CardHeader>
        <CardContent>
          <div className="overflow-x-auto rounded-[24px] border border-border/70 bg-background/25">
            <table className="w-full min-w-[1120px] border-collapse text-sm">
            <thead>
              <tr className="border-b border-border text-left text-muted-foreground">
                <th className="px-4 py-3 pr-2">Time</th>
                <th className="px-4 py-3 pr-2">Action</th>
                <th className="px-4 py-3 pr-2">Entity</th>
                <th className="px-4 py-3 pr-2">Actor</th>
                <th className="px-4 py-3 pr-2">Summary</th>
                <th className="px-4 py-3 pr-4">Details</th>
              </tr>
            </thead>
            <tbody>
              {items.length === 0 ? (
                <tr>
                  <td colSpan={6} className="px-4 py-6 text-muted-foreground">No audit entries found.</td>
                </tr>
              ) : (
                items.map((item) => (
                  <tr key={item.id} className="border-b border-border/60">
                    <td className="px-4 py-3 pr-2 text-muted-foreground">{new Date(item.timestamp).toLocaleString()}</td>
                    <td className="px-4 py-3 pr-2">
                      <Badge className={actionBadgeClassName(item.action)}>{item.action}</Badge>
                    </td>
                    <td className="px-4 py-3 pr-2">
                      <div>
                        <p className="font-medium">{formatEntityType(item.entityType)}</p>
                        <p className="text-xs text-muted-foreground">{item.entityLabel ?? item.entityId}</p>
                      </div>
                    </td>
                    <td className="px-4 py-3 pr-2">
                      <div>
                        <p className="font-medium">{item.userDisplayName ?? 'Unknown operator'}</p>
                        <p className="text-xs text-muted-foreground">{item.userId}</p>
                      </div>
                    </td>
                    <td className="px-4 py-3 pr-2 text-muted-foreground">{summarizeEntry(item)}</td>
                    <td className="px-4 py-3 pr-4">
                      <button
                        type="button"
                        className="rounded-full border border-input px-3 py-1.5 text-xs hover:bg-muted"
                        onClick={() => {
                          setSelected(item)
                        }}
                      >
                        View JSON
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
            </table>
          </div>
          <PaginationControls
            page={page}
            pageSize={pageSize}
            totalCount={totalCount}
            totalPages={totalPages}
            onPageChange={onPageChange}
            onPageSizeChange={onPageSizeChange}
          />
        </CardContent>
      </Card>

      <AuditDetailDialog
        selected={selected}
        onClose={() => {
          setSelected(null)
        }}
      />
    </>
  )
}

function summarizeEntry(item: AuditLogItem) {
  const values = parseValues(item.newValues)
  const previous = parseValues(item.oldValues)
  const changedKeys = [...new Set([...Object.keys(values), ...Object.keys(previous)])]

  if (item.action === 'Created') {
    return 'Created entry.'
  }

  if (item.action === 'Deleted') {
    return 'Deleted entry.'
  }

  if (!changedKeys.length) {
    return 'Updated entry.'
  }

  const preview = changedKeys.slice(0, 3).map(formatKey).join(', ')
  const suffix = changedKeys.length > 3 ? ` +${changedKeys.length - 3} more` : ''
  return `Changed ${preview}${suffix}`
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
  return value.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/Id\b/g, 'ID')
}

function formatKey(value: string) {
  return value.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/Id\b/g, 'ID').toLowerCase()
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
