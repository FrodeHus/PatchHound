import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/audit-log/')({
  component: AuditLogPage,
})

function AuditLogPage() {
  return (
    <section className="space-y-2">
      <h1 className="text-2xl font-semibold">Audit Log</h1>
      <p className="text-sm text-muted-foreground">Immutable change history view will be added in a subsequent task.</p>
    </section>
  )
}
