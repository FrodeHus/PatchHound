import { createFileRoute, redirect } from '@tanstack/react-router'
import { StoredCredentialsManagement } from '@/components/features/admin/StoredCredentialsManagement'

export const Route = createFileRoute('/_authed/admin/platform/credentials')({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes('GlobalAdmin')) {
      throw redirect({ to: '/admin' })
    }
  },
  component: StoredCredentialsPage,
})

function StoredCredentialsPage() {
  return (
    <section className="space-y-6 pb-4">
      <div className="space-y-2">
        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
          Platform configuration
        </p>
        <h1 className="text-3xl font-semibold tracking-[-0.04em]">
          Stored Credentials
        </h1>
        <p className="text-sm text-muted-foreground">
          Create, scope, rotate, and remove reusable credential references for
          ingestion sources and platform integrations.
        </p>
      </div>

      <StoredCredentialsManagement />
    </section>
  )
}
