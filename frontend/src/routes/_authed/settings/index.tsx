import { createFileRoute } from '@tanstack/react-router'
import { fetchTenants } from '@/api/settings.functions'
import { TenantManagement } from '@/components/features/settings/TenantManagement'

export const Route = createFileRoute('/_authed/settings/')({
  loader: () => fetchTenants({ data: {} }),
  component: SettingsPage,
})

function SettingsPage() {
  const data = Route.useLoaderData()

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Settings</h1>
      <TenantManagement data={data} />
    </section>
  )
}
