import { createFileRoute } from '@tanstack/react-router'
import { useMemo, useState } from 'react'
import { AuditLogTable } from '@/components/features/audit/AuditLogTable'
import { useAuditLog, type AuditLogFilters } from '@/api/useAuditLog'

export const Route = createFileRoute('/audit-log/')({
  component: AuditLogPage,
})

function AuditLogPage() {
  const [entityType, setEntityType] = useState('')
  const [action, setAction] = useState('')

  const filters = useMemo<AuditLogFilters>(
    () => ({ entityType: entityType || undefined, action: action || undefined, page: 1, pageSize: 100 }),
    [action, entityType],
  )
  const auditQuery = useAuditLog(filters)

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Audit Log</h1>

      <div className="grid gap-2 rounded-lg border border-border bg-card p-4 md:grid-cols-2">
        <input
          className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
          placeholder="Entity type (e.g. Vulnerability)"
          value={entityType}
          onChange={(event) => {
            setEntityType(event.target.value)
          }}
        />
        <select
          className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
          value={action}
          onChange={(event) => {
            setAction(event.target.value)
          }}
        >
          <option value="">All actions</option>
          <option value="Created">Created</option>
          <option value="Updated">Updated</option>
          <option value="Deleted">Deleted</option>
        </select>
      </div>

      {auditQuery.isLoading ? <p className="text-sm text-muted-foreground">Loading audit entries...</p> : null}
      {auditQuery.isError ? <p className="text-sm text-destructive">Failed to load audit entries.</p> : null}
      {auditQuery.data ? <AuditLogTable items={auditQuery.data.items} totalCount={auditQuery.data.totalCount} /> : null}
    </section>
  )
}
