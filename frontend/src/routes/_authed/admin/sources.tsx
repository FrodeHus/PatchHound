import { useEffect, useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import { useMutation } from '@tanstack/react-query'
import { fetchTenantDetail, fetchTenants } from '@/api/settings.functions'
import { TenantSourceManagement } from '@/components/features/admin/TenantSourceManagement'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

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
  const { tenants, initialTenant } = Route.useLoaderData()
  const [selectedTenantId, setSelectedTenantId] = useState(initialTenant?.id ?? '')

  const tenantMutation = useMutation({
    mutationFn: async (tenantId: string) => fetchTenantDetail({ data: { tenantId } }),
  })

  useEffect(() => {
    if (!selectedTenantId || initialTenant?.id === selectedTenantId) {
      return
    }

    void tenantMutation.mutateAsync(selectedTenantId)
  }, [initialTenant?.id, selectedTenantId])

  const tenant = tenantMutation.data ?? (initialTenant?.id === selectedTenantId ? initialTenant : null)

  return (
    <section className="space-y-5 pb-4">
      <div className="space-y-1">
        <h1 className="text-3xl font-semibold tracking-[-0.04em]">Sources</h1>
        <p className="text-sm text-muted-foreground">
          Manage tenant ingestion sources and enrichment providers without mixing them into tenant identity administration.
        </p>
      </div>

      <Card className="rounded-[28px] border-border/70 bg-card/82">
        <CardHeader>
          <CardTitle>Tenant Context</CardTitle>
        </CardHeader>
        <CardContent className="grid gap-4 md:grid-cols-[minmax(0,1fr)_auto] md:items-end">
          <label className="space-y-2">
            <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Selected Tenant</span>
            <select
              className="rounded-md border border-input bg-background px-3 py-2 text-sm"
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
            <div className="text-sm text-muted-foreground">
              {tenant.assets.totalCount} assets across {tenant.assets.deviceCount} devices, {tenant.assets.softwareCount} software records, and {tenant.assets.cloudResourceCount} cloud resources.
            </div>
          ) : null}
        </CardContent>
      </Card>

      {tenant ? (
        <TenantSourceManagement key={tenant.id} tenant={tenant} />
      ) : (
        <Card className="rounded-[28px] border-border/70 bg-card/82">
          <CardContent className="py-8 text-sm text-muted-foreground">
            No tenants are registered yet.
          </CardContent>
        </Card>
      )}
    </section>
  )
}
