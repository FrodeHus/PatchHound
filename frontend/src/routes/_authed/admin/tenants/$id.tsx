import { createFileRoute } from '@tanstack/react-router'
import { fetchTenantDetail } from '@/api/settings.functions'
import { TenantAdministrationDetail } from '@/components/features/admin/TenantAdministrationDetail'

export const Route = createFileRoute('/_authed/admin/tenants/$id')({
  loader: ({ params }) => fetchTenantDetail({ data: { tenantId: params.id } }),
  component: TenantDetailPage,
})

function TenantDetailPage() {
  const tenant = Route.useLoaderData()

  return <TenantAdministrationDetail key={tenant.id} tenant={tenant} />
}
