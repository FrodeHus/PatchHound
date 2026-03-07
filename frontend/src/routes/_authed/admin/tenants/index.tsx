import { createFileRoute } from '@tanstack/react-router'
import { fetchTenants } from '@/api/settings.functions'
import { TenantAdministrationList } from '@/components/features/admin/TenantAdministrationList'

export const Route = createFileRoute('/_authed/admin/tenants/')({
  loader: () => fetchTenants({ data: {} }),
  component: TenantAdministrationPage,
})

function TenantAdministrationPage() {
  const data = Route.useLoaderData()

  return <TenantAdministrationList tenants={data.items} totalCount={data.totalCount} />
}
