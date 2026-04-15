import { createFileRoute, redirect } from '@tanstack/react-router'
import { SentinelConnectorCard } from '@/components/features/admin/SentinelConnectorCard'

export const Route = createFileRoute('/_authed/admin/platform/integrations')({
  beforeLoad: ({ context }) => {
    if (!(context.user?.activeRoles ?? []).includes('GlobalAdmin')) {
      throw redirect({ to: '/admin' })
    }
  },
  component: IntegrationsPage,
})

function IntegrationsPage() {
  return (
    <section className="space-y-5">
      <div className="rounded-[32px] border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,transparent),transparent_55%),var(--color-card)] p-6">
        <div className="space-y-2">
          <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
            Platform configuration
          </p>
          <h1 className="text-3xl font-semibold tracking-[-0.04em]">
            Integrations
          </h1>
          <p className="max-w-2xl text-sm text-muted-foreground">
            Configure external service connectors for forwarding data to third-party platforms.
          </p>
        </div>
      </div>

      <SentinelConnectorCard />
    </section>
  )
}
