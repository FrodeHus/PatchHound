import { useState } from 'react'
import type { AuditLogItem } from '@/api/useAuditLog'
import { AuditDetailDialog } from '@/components/features/audit/AuditDetailDialog'

type AuditLogTableProps = {
  items: AuditLogItem[]
  totalCount: number
}

export function AuditLogTable({ items, totalCount }: AuditLogTableProps) {
  const [selected, setSelected] = useState<AuditLogItem | null>(null)

  return (
    <>
      <section className="rounded-lg border border-border bg-card p-4">
        <div className="mb-3 flex items-end justify-between">
          <h2 className="text-lg font-semibold">Audit Log</h2>
          <p className="text-xs text-muted-foreground">{totalCount} total</p>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full min-w-[980px] border-collapse text-sm">
            <thead>
              <tr className="border-b border-border text-left text-muted-foreground">
                <th className="py-2 pr-2">Time</th>
                <th className="py-2 pr-2">Entity Type</th>
                <th className="py-2 pr-2">Action</th>
                <th className="py-2 pr-2">Entity ID</th>
                <th className="py-2 pr-2">User ID</th>
                <th className="py-2 pr-2">Details</th>
              </tr>
            </thead>
            <tbody>
              {items.length === 0 ? (
                <tr>
                  <td colSpan={6} className="py-3 text-muted-foreground">No audit entries found.</td>
                </tr>
              ) : (
                items.map((item) => (
                  <tr key={item.id} className="border-b border-border/60">
                    <td className="py-2 pr-2">{new Date(item.timestamp).toLocaleString()}</td>
                    <td className="py-2 pr-2">{item.entityType}</td>
                    <td className="py-2 pr-2">{item.action}</td>
                    <td className="py-2 pr-2"><code>{item.entityId}</code></td>
                    <td className="py-2 pr-2"><code>{item.userId}</code></td>
                    <td className="py-2 pr-2">
                      <button
                        type="button"
                        className="rounded-md border border-input px-2 py-1 text-xs hover:bg-muted"
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
      </section>

      <AuditDetailDialog
        selected={selected}
        onClose={() => {
          setSelected(null)
        }}
      />
    </>
  )
}
