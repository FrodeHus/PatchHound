import { createFileRoute } from '@tanstack/react-router'
import { fetchAssets } from '@/api/assets.functions'
import { AssetManagementTable } from '@/components/features/assets/AssetManagementTable'

export const Route = createFileRoute('/_authed/assets/')({
  loader: () => fetchAssets({ data: {} }),
  component: AssetsPage,
})

function AssetsPage() {
  const data = Route.useLoaderData()

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Assets</h1>
      <AssetManagementTable data={data} />
    </section>
  )
}
