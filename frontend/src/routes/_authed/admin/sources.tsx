import { createFileRoute, redirect } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { z } from 'zod'
import { fetchAuditLog } from '@/api/audit-log.functions'
import { fetchTenantDetail } from '@/api/settings.functions'
import { RecentAuditPanel } from '@/components/features/audit/RecentAuditPanel'
import { GlobalEnrichmentSourceManagement } from '@/components/features/admin/GlobalEnrichmentSourceManagement'
import { TenantSourceManagement } from '@/components/features/admin/TenantSourceManagement'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { InsetPanel } from '@/components/ui/inset-panel'
import { fetchEnrichmentSources } from '@/server/system.functions'
import { cn } from '@/lib/utils'

export const Route = createFileRoute('/_authed/admin/sources')({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes('GlobalAdmin') && !activeRoles.includes('SecurityManager')) {
      throw redirect({ to: '/admin' })
    }
  },
  validateSearch: z.object({
    activeView: z.enum(['tenant', 'global-enrichment']).optional(),
    mode: z.enum(['edit', 'history']).optional(),
    sourceKey: z.string().optional(),
  }),
  component: SourcesAdministrationPage,
})

function SourcesAdministrationPage() {
  const navigate = Route.useNavigate()
  const search = Route.useSearch()
  const { user } = Route.useRouteContext()
  const { selectedTenantId, tenants } = useTenantScope()
  const activeView = search.activeView ?? 'tenant'
  const canManageEnrichment = hasGlobalEnrichmentAccess(user.activeRoles ?? [])
  const tenantQuery = useQuery({
    queryKey: ['tenant-detail', selectedTenantId],
    queryFn: () => fetchTenantDetail({ data: { tenantId: selectedTenantId! } }),
    enabled: activeView === 'tenant' && Boolean(selectedTenantId),
    refetchInterval: (query) => {
      const tenant = query.state.data
      if (!tenant) {
        return false
      }

      return tenant.ingestionSources.some((source) => source.runtime.activeIngestionRunId) ? 3000 : false
    },
  })
  const enrichmentQuery = useQuery({
    queryKey: ['enrichment-sources'],
    queryFn: () => fetchEnrichmentSources(),
    enabled: canManageEnrichment,
  })
  const canViewAudit = (user.activeRoles ?? []).includes('GlobalAdmin') || (user.activeRoles ?? []).includes('Auditor')
  const tenantAuditQuery = useQuery({
    queryKey: ['tenant-source-audit', selectedTenantId],
    queryFn: () =>
      fetchAuditLog({
        data: {
          tenantId: selectedTenantId!,
          entityType: 'TenantSourceConfiguration',
          page: 1,
          pageSize: 5,
        },
      }),
    enabled: canViewAudit && activeView === 'tenant' && Boolean(selectedTenantId),
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
    enabled: canViewAudit && activeView === 'global-enrichment' && canManageEnrichment,
  })
  const tenant = tenantQuery.data ?? null
  const enrichmentSources = enrichmentQuery.data ?? []

  return (
    <section className="space-y-6 pb-4">
      <div className="space-y-2">
        <h1 className="text-3xl font-semibold tracking-[-0.04em]">
          Sources Console
        </h1>
        <p className="text-sm text-muted-foreground">
          Manage tenant-specific ingestion separately from shared global
          enrichment, with a cleaner operational split between the two.
        </p>
      </div>

      {canManageEnrichment ? (
        <div className="inline-flex rounded-xl border border-border/70 bg-card/70 p-1">
          <button
            type="button"
            className={viewToggleClassName(activeView === "tenant")}
            onClick={() => {
              void navigate({
                to: '/admin/sources',
                search: {
                  activeView: 'tenant',
                },
              })
            }}
          >
            Tenant Sources
          </button>
          <button
            type="button"
            className={viewToggleClassName(activeView === "global-enrichment")}
            onClick={() => {
              void navigate({
                to: '/admin/sources',
                search: {
                  activeView: 'global-enrichment',
                },
              })
            }}
          >
            Global Enrichment
          </button>
        </div>
      ) : null}

      {activeView === "tenant" ? (
        <Card className="rounded-2xl border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_94%,black),var(--card))] shadow-sm">
          <CardHeader className="border-b border-border/60 pb-5">
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div className="space-y-1">
                <CardTitle>Tenant Context</CardTitle>
                <p className="text-sm text-muted-foreground">
                  The top bar controls which tenant's ingestion credentials,
                  schedules, and manual sync controls are active here.
                </p>
              </div>
              {tenant ? (
                <Badge
                  variant="outline"
                  className="rounded-full border-primary/20 bg-primary/10 px-3 py-1 text-primary"
                >
                  {tenant.name}
                </Badge>
              ) : null}
            </div>
          </CardHeader>
          <CardContent className="grid gap-4 pt-5 lg:grid-cols-[minmax(280px,360px)_minmax(0,1fr)] lg:items-end">
            {tenant ? (
              <div className="grid gap-3 sm:grid-cols-3">
                <InsetPanel className="px-4 py-3">
                  <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                    Assets
                  </p>
                  <p className="mt-1 text-xl font-semibold">
                    {tenant.assets.totalCount}
                  </p>
                </InsetPanel>
                <InsetPanel className="px-4 py-3">
                  <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                    Devices
                  </p>
                  <p className="mt-1 text-xl font-semibold">
                    {tenant.assets.deviceCount}
                  </p>
                </InsetPanel>
                <InsetPanel className="px-4 py-3">
                  <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">
                    Software
                  </p>
                  <p className="mt-1 text-xl font-semibold">
                    {tenant.assets.softwareCount}
                  </p>
                </InsetPanel>
              </div>
            ) : null}
          </CardContent>
          {tenant ? (
            <CardContent className="pt-0">
              <InsetPanel emphasis="subtle" className="border-dashed px-4 py-3 text-sm text-muted-foreground">
                Cloud resources: {tenant.assets.cloudResourceCount}. Global
                enrichment is managed separately and applied automatically
                during worker processing.
              </InsetPanel>
            </CardContent>
          ) : null}
        </Card>
      ) : null}

      {activeView === "tenant" && !tenants.length ? (
        <Card className="rounded-2xl">
          <CardContent className="py-8 text-sm text-muted-foreground">
            No tenants are registered yet.
          </CardContent>
        </Card>
      ) : null}

      {activeView === "tenant" && tenants.length > 0 && !selectedTenantId ? (
        <Card className="rounded-2xl">
          <CardContent className="py-8 text-sm text-muted-foreground">
            No tenant scope is active. Choose a tenant from the top bar.
          </CardContent>
        </Card>
      ) : null}

      {activeView === "tenant" && selectedTenantId && tenantQuery.isPending ? (
        <Card className="rounded-2xl">
          <CardContent className="py-8 text-sm text-muted-foreground">
            Loading tenant source configuration...
          </CardContent>
        </Card>
      ) : null}

      {activeView === "tenant" && tenant ? (
        <TenantSourceManagement
          key={tenant.id}
          tenant={tenant}
          editingSourceKey={search.mode === 'edit' ? search.sourceKey ?? null : null}
          historySourceKey={search.mode === 'history' ? search.sourceKey ?? null : null}
          onEditSource={(sourceKey) => {
            void navigate({
              to: '/admin/sources',
              search: {
                activeView: 'tenant',
                mode: 'edit',
                sourceKey,
              },
            })
          }}
          onOpenHistory={(sourceKey) => {
            void navigate({
              to: '/admin/sources',
              search: {
                activeView: 'tenant',
                mode: 'history',
                sourceKey,
              },
            })
          }}
          onCloseHistory={() => {
            void navigate({
              to: '/admin/sources',
              search: {
                activeView: 'tenant',
              },
            })
          }}
          onCloseEditor={() => {
            void navigate({
              to: '/admin/sources',
              search: {
                activeView: 'tenant',
              },
            })
          }}
        />
      ) : null}

      {activeView === "tenant" && tenant && canViewAudit ? (
        <RecentAuditPanel
          title="Source Activity"
          description="Recent ingestion source configuration changes for the selected tenant."
          items={tenantAuditQuery.data?.items ?? []}
          emptyMessage="No recent tenant source changes have been recorded for this tenant."
        />
      ) : null}

      {activeView === "global-enrichment" && canManageEnrichment ? (
        enrichmentQuery.data ? (
          <div className="space-y-6">
            <GlobalEnrichmentSourceManagement
              key={enrichmentSources.map((source) => source.key).join(":")}
              sources={enrichmentSources}
              onSaved={async () => {
                await enrichmentQuery.refetch();
                if (canViewAudit) {
                  await enrichmentAuditQuery.refetch();
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
          </div>
        ) : (
          <Card className="rounded-2xl">
            <CardContent className="py-8 text-sm text-muted-foreground">
              Loading global enrichment providers...
            </CardContent>
          </Card>
        )
      ) : null}
    </section>
  );
}

function hasGlobalEnrichmentAccess(roles: string[]) {
  return roles.includes('GlobalAdmin')
}

function viewToggleClassName(isActive: boolean) {
  return cn(
    'rounded-2xl px-4 py-2 text-sm font-medium transition-colors',
    isActive
      ? 'bg-background text-foreground shadow-sm'
      : 'text-muted-foreground hover:text-foreground',
  )
}
