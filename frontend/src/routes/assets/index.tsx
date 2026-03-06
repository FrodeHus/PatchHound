import { createFileRoute } from '@tanstack/react-router'
import { useMemo, useState } from 'react'
import {
  useAssets,
  useAssignAssetOwner,
  useSetAssetCriticality,
  type AssetFilters,
} from '@/api/useAssets'
import { AssetManagementTable } from '@/components/features/assets/AssetManagementTable'

export const Route = createFileRoute('/assets/')({
  component: AssetsPage,
})

function AssetsPage() {
  const [search, setSearch] = useState('')
  const [assetType, setAssetType] = useState('')
  const [ownerType, setOwnerType] = useState('')

  const filters = useMemo<AssetFilters>(() => ({
    search: search.trim() || undefined,
    assetType: assetType || undefined,
    ownerType: ownerType || undefined,
    page: 1,
    pageSize: 50,
  }), [search, assetType, ownerType])

  const assetsQuery = useAssets(filters)
  const assignOwnerMutation = useAssignAssetOwner()
  const setCriticalityMutation = useSetAssetCriticality()
  const isUpdating = assignOwnerMutation.isPending || setCriticalityMutation.isPending

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Assets</h1>

      <div className="grid gap-2 rounded-lg border border-border bg-card p-4 md:grid-cols-3">
        <input
          className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
          placeholder="Search asset name or external ID"
          value={search}
          onChange={(event) => {
            setSearch(event.target.value)
          }}
        />
        <select
          className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
          value={assetType}
          onChange={(event) => {
            setAssetType(event.target.value)
          }}
        >
          <option value="">All asset types</option>
          <option value="Device">Device</option>
          <option value="Software">Software</option>
          <option value="CloudResource">Cloud Resource</option>
        </select>
        <select
          className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
          value={ownerType}
          onChange={(event) => {
            setOwnerType(event.target.value)
          }}
        >
          <option value="">All owner types</option>
          <option value="User">User</option>
          <option value="Team">Team</option>
        </select>
      </div>

      {assetsQuery.isLoading ? <p className="text-sm text-muted-foreground">Loading assets...</p> : null}
      {assetsQuery.isError ? <p className="text-sm text-destructive">Failed to load assets.</p> : null}

      {assetsQuery.data ? (
        <AssetManagementTable
          assets={assetsQuery.data.items}
          totalCount={assetsQuery.data.totalCount}
          isUpdating={isUpdating}
          onAssignOwner={(assetId, selectedOwnerType, ownerId) => {
            assignOwnerMutation.mutate({
              assetId,
              ownerType: selectedOwnerType,
              ownerId,
            })
          }}
          onSetCriticality={(assetId, criticality) => {
            setCriticalityMutation.mutate({
              assetId,
              criticality,
            })
          }}
        />
      ) : null}
    </section>
  )
}
