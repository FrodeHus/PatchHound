import { useEffect, useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { fetchTenantDetail, fetchTenants } from '@/api/settings.functions'
import { GlobalEnrichmentSourceManagement } from '@/components/features/admin/GlobalEnrichmentSourceManagement'
import { TenantSourceManagement } from '@/components/features/admin/TenantSourceManagement'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { fetchEnrichmentSources } from '@/server/system.functions'
import { cn } from '@/lib/utils'

export const Route = createFileRoute('/_authed/admin/sources')({
  loader: async () => {
    const tenants = await fetchTenants({ data: { page: 1, pageSize: 100 } })
    const initialTenantId = tenants.items[0]?.id
    const initialTenant = initialTenantId
      ? await fetchTenantDetail({ data: { tenantId: initialTenantId } })
      : null

    return {
      tenants: tenants.items,
      initialTenant,
    }
  },
  component: SourcesAdministrationPage,
})

function SourcesAdministrationPage() {
  const { user } = Route.useRouteContext()
  const { tenants, initialTenant } = Route.useLoaderData()
  const [selectedTenantId, setSelectedTenantId] = useState(initialTenant?.id ?? '')
  const [activeView, setActiveView] = useState<'tenant' | 'global-enrichment'>('tenant')

  const tenantMutation = useMutation({
    mutationFn: async (tenantId: string) => fetchTenantDetail({ data: { tenantId } }),
  })
  const enrichmentMutation = useMutation({
    mutationFn: async () => fetchEnrichmentSources(),
  })
  const canManageGlobalEnrichment = user.roles.includes('GlobalAdmin')

  useEffect(() => {
    if (!selectedTenantId || initialTenant?.id === selectedTenantId) {
      return
    }

    void tenantMutation.mutateAsync(selectedTenantId)
  }, [initialTenant?.id, selectedTenantId])

  useEffect(() => {
    if (!canManageGlobalEnrichment) {
      return
    }

    if (enrichmentMutation.data || enrichmentMutation.isPending) {
      return
    }

    void enrichmentMutation.mutateAsync()
  }, [canManageGlobalEnrichment, enrichmentMutation])

  const tenant = tenantMutation.data ?? (initialTenant?.id === selectedTenantId ? initialTenant : null)
  const enrichmentSources = enrichmentMutation.data ?? []

  return (
    <section className="space-y-6 pb-4">
      <div className="space-y-2">
        <h1 className="text-3xl font-semibold tracking-[-0.04em]">Sources Console</h1>
        <p className="text-sm text-muted-foreground">
          Manage tenant-specific ingestion separately from shared global enrichment, with a cleaner operational split between the two.
        </p>
      </div>

      {canManageGlobalEnrichment ? (
        <div className="inline-flex rounded-[20px] border border-border/70 bg-card/70 p-1">
          <button
            type="button"
            className={viewToggleClassName(activeView === 'tenant')}
            onClick={() => setActiveView('tenant')}
          >
            Tenant Sources
          </button>
          <button
            type="button"
            className={viewToggleClassName(activeView === 'global-enrichment')}
            onClick={() => setActiveView('global-enrichment')}
          >
            Global Enrichment
          </button>
        </div>
      ) : null}

      {activeView === 'tenant' ? (
        <Card className="rounded-[30px] border-border/70 bg-card/82 shadow-sm">
          <CardHeader className="border-b border-border/60 pb-5">
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div className="space-y-1">
                <CardTitle>Tenant Context</CardTitle>
                <p className="text-sm text-muted-foreground">
                  Pick the tenant whose ingestion credentials, schedules, and manual sync controls you want to manage.
                </p>
              </div>
              {tenant ? (
                <Badge variant="outline" className="rounded-full border-primary/20 bg-primary/10 px-3 py-1 text-primary">
                  {tenant.name}
                </Badge>
              ) : null}
            </div>
          </CardHeader>
          <CardContent className="grid gap-4 pt-5 lg:grid-cols-[minmax(280px,360px)_minmax(0,1fr)] lg:items-end">
            <label className="space-y-2">
              <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Selected Tenant</span>
              <select
                className="w-full rounded-xl border border-input bg-background px-3 py-2.5 text-sm"
                value={selectedTenantId}
                onChange={(event) => setSelectedTenantId(event.target.value)}
              >
                {tenants.map((tenantItem) => (
                  <option key={tenantItem.id} value={tenantItem.id}>
                    {tenantItem.name}
                  </option>
                ))}
              </select>
            </label>
            {tenant ? (
              <div className="grid gap-3 sm:grid-cols-3">
                <div className="rounded-2xl border border-border/70 bg-background/35 px-4 py-3">
                  <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Assets</p>
                  <p className="mt-1 text-xl font-semibold">{tenant.assets.totalCount}</p>
                </div>
                <div className="rounded-2xl border border-border/70 bg-background/35 px-4 py-3">
                  <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Devices</p>
                  <p className="mt-1 text-xl font-semibold">{tenant.assets.deviceCount}</p>
                </div>
                <div className="rounded-2xl border border-border/70 bg-background/35 px-4 py-3">
                  <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">Software</p>
                  <p className="mt-1 text-xl font-semibold">{tenant.assets.softwareCount}</p>
                </div>
              </div>
            ) : null}
          </CardContent>
          {tenant ? (
            <CardContent className="pt-0">
              <div className="rounded-2xl border border-dashed border-border/60 bg-background/20 px-4 py-3 text-sm text-muted-foreground">
                Cloud resources: {tenant.assets.cloudResourceCount}. Global enrichment is managed separately and applied automatically during worker processing.
              </div>
            </CardContent>
          ) : null}
        </Card>
      ) : null}

      {activeView === 'tenant' && !tenants.length ? (
        <Card className="rounded-[28px] border-border/70 bg-card/82">
          <CardContent className="py-8 text-sm text-muted-foreground">
            No tenants are registered yet.
          </CardContent>
        </Card>
      ) : null}

      {activeView === 'tenant' && tenant ? <TenantSourceManagement key={tenant.id} tenant={tenant} /> : null}

      {activeView === 'global-enrichment' && canManageGlobalEnrichment ? (
        enrichmentMutation.data ? (
          <GlobalEnrichmentSourceManagement
            key={enrichmentSources.map((source) => source.key).join(':')}
            sources={enrichmentSources}
            onSaved={async () => {
              await enrichmentMutation.mutateAsync()
            }}
          />
        ) : (
          <Card className="rounded-[28px] border-border/70 bg-card/82">
            <CardContent className="py-8 text-sm text-muted-foreground">
              Loading global enrichment providers...
            </CardContent>
          </Card>
        )
      ) : null}
    </section>
  )
}

function viewToggleClassName(isActive: boolean) {
  return cn(
    'rounded-2xl px-4 py-2 text-sm font-medium transition-colors',
    isActive
      ? 'bg-background text-foreground shadow-sm'
      : 'text-muted-foreground hover:text-foreground',
  )
}
