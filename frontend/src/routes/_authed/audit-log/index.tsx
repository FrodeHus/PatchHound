import { createFileRoute } from '@tanstack/react-router'
import { fetchAuditLog } from '@/api/audit-log.functions'
import { AuditLogTable } from '@/components/features/audit/AuditLogTable'

export const Route = createFileRoute('/_authed/audit-log/')({
  loader: () => fetchAuditLog({ data: {} }),
  component: AuditLogPage,
})

function AuditLogPage() {
  const data = Route.useLoaderData()

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Audit Log</h1>
      <AuditLogTable items={data.items} totalCount={data.totalCount} />
    </section>
  )
}
