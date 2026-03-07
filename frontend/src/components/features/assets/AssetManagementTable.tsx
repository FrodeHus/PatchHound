import { useState } from 'react'
import type { Asset } from '@/api/assets.schemas'

type AssetManagementTableProps = {
  assets: Asset[]
  totalCount: number
  isUpdating: boolean
  onAssignOwner: (assetId: string, ownerType: 'User' | 'Team', ownerId: string) => void
  onSetCriticality: (assetId: string, criticality: string) => void
}

const criticalityOptions = ['Low', 'Medium', 'High', 'Critical']

export function AssetManagementTable({
  assets,
  totalCount,
  isUpdating,
  onAssignOwner,
  onSetCriticality,
}: AssetManagementTableProps) {
  const [ownerType, setOwnerType] = useState<'User' | 'Team'>('User')
  const [ownerId, setOwnerId] = useState('')

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <div className="mb-3 flex items-end justify-between">
        <h2 className="text-lg font-semibold">Assets</h2>
        <p className="text-xs text-muted-foreground">{totalCount} total</p>
      </div>

      <div className="mb-3 grid gap-2 rounded-md border border-border/70 bg-muted/30 p-3 md:grid-cols-[120px_1fr_auto]">
        <select
          className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
          value={ownerType}
          onChange={(event) => {
            if (event.target.value === 'Team') {
              setOwnerType('Team')
              return
            }

            setOwnerType('User')
          }}
        >
          <option value="User">User</option>
          <option value="Team">Team</option>
        </select>
        <input
          className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
          value={ownerId}
          onChange={(event) => {
            setOwnerId(event.target.value)
          }}
          placeholder="Owner GUID"
        />
        <p className="self-center text-xs text-muted-foreground">Used for "Assign Owner" action per row.</p>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full min-w-[980px] border-collapse text-sm">
          <thead>
            <tr className="border-b border-border text-left text-muted-foreground">
              <th className="py-2 pr-2">Name</th>
              <th className="py-2 pr-2">External ID</th>
              <th className="py-2 pr-2">Type</th>
              <th className="py-2 pr-2">Owner Type</th>
              <th className="py-2 pr-2">Criticality</th>
              <th className="py-2 pr-2">Vulnerabilities</th>
              <th className="py-2 pr-2">Actions</th>
            </tr>
          </thead>
          <tbody>
            {assets.length === 0 ? (
              <tr>
                <td colSpan={7} className="py-3 text-muted-foreground">
                  No assets found.
                </td>
              </tr>
            ) : (
              assets.map((asset) => (
                <tr key={asset.id} className="border-b border-border/60">
                  <td className="py-2 pr-2 font-medium">{asset.name}</td>
                  <td className="py-2 pr-2">{asset.externalId}</td>
                  <td className="py-2 pr-2">{asset.assetType}</td>
                  <td className="py-2 pr-2">{asset.ownerType}</td>
                  <td className="py-2 pr-2">
                    <select
                      className="rounded-md border border-input bg-background px-2 py-1 text-sm"
                      defaultValue={asset.criticality}
                      onChange={(event) => {
                        onSetCriticality(asset.id, event.target.value)
                      }}
                      disabled={isUpdating}
                    >
                      {criticalityOptions.map((value) => (
                        <option key={value} value={value}>
                          {value}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td className="py-2 pr-2">{asset.vulnerabilityCount}</td>
                  <td className="py-2 pr-2">
                    <button
                      type="button"
                      className="rounded-md border border-input px-2 py-1 text-xs hover:bg-muted disabled:opacity-50"
                      disabled={isUpdating || ownerId.trim().length === 0}
                      onClick={() => {
                        onAssignOwner(asset.id, ownerType, ownerId)
                      }}
                    >
                      Assign Owner
                    </button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </section>
  )
}
