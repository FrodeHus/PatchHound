import { createFileRoute, redirect } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { fetchAuditLog } from '@/api/audit-log.functions'
import { GlobalEnrichmentSourceManagement } from '@/components/features/admin/GlobalEnrichmentSourceManagement'
import { RecentAuditPanel } from '@/components/features/audit/RecentAuditPanel'
import { fetchEnrichmentSources } from '@/server/system.functions'

export const Route = createFileRoute('/_authed/admin/platform/enrichment')({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes('GlobalAdmin')) {
      throw redirect({ to: '/admin' })
    }
  },
  loader: () => fetchEnrichmentSources(),
  component: EnrichmentSourcesPage,
})

function EnrichmentSourcesPage() {
  const { user } = Route.useRouteContext()
  const initialSources = Route.useLoaderData()
  const canViewAudit =
    (user.activeRoles ?? []).includes('GlobalAdmin') ||
    (user.activeRoles ?? []).includes('Auditor')

  const enrichmentQuery = useQuery({
    queryKey: ['enrichment-sources'],
    queryFn: () => fetchEnrichmentSources(),
    initialData: initialSources,
    staleTime: 30_000,
  })

  const enrichmentAuditQuery = useQuery({
    queryKey: ['enrichment-source-audit'],
    queryFn: () =>
      fetchAuditLog({
        data: {
          entityType: 'EnrichmentSourceConfiguration',
          page: 1,
          pageSize: 5,
        },
      }),
    enabled: canViewAudit,
    staleTime: 30_000,
  })

  const sources = enrichmentQuery.data ?? []

  return (
    <section className="space-y-6 pb-4">
      <div className="space-y-2">
        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
          Platform configuration
        </p>
        <h1 className="text-3xl font-semibold tracking-[-0.04em]">
          Enrichment Sources
        </h1>
        <p className="text-sm text-muted-foreground">
          Configure shared global enrichment providers such as NVD that are
          applied automatically across all tenants during worker processing.
        </p>
      </div>

      <GlobalEnrichmentSourceManagement
        key={sources.map((source) => source.key).join(':')}
        sources={sources}
        onSaved={async () => {
          await enrichmentQuery.refetch()
          if (canViewAudit) {
            await enrichmentAuditQuery.refetch()
          }
        }}
      />

      {canViewAudit ? (
        <RecentAuditPanel
          title="Enrichment Activity"
          description="Recent changes to shared enrichment providers such as NVD."
          items={enrichmentAuditQuery.data?.items ?? []}
          emptyMessage="No recent enrichment source changes have been recorded."
        />
      ) : null}
    </section>
  )
}
