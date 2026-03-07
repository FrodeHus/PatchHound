import type { Asset } from '@/api/assets.schemas'
import type { TeamDetail } from '@/api/teams.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'

type AssignmentGroupDetailViewProps = {
  team: TeamDetail
  assets: Asset[]
  totalAssetCount: number
  selectedAssetIds: string[]
  filters: {
    search: string
    assetType: string
    criticality: string
  }
  isLoadingAssets: boolean
  isAssigningAssets: boolean
  onFilterChange: (next: { search: string; assetType: string; criticality: string }) => void
  onToggleAsset: (assetId: string) => void
  onToggleAllVisible: () => void
  onAssignSelected: () => void
}

export function AssignmentGroupDetailView({
  team,
  assets,
  totalAssetCount,
  selectedAssetIds,
  filters,
  isLoadingAssets,
  isAssigningAssets,
  onFilterChange,
  onToggleAsset,
  onToggleAllVisible,
  onAssignSelected,
}: AssignmentGroupDetailViewProps) {
  const allVisibleSelected = assets.length > 0 && assets.every((asset) => selectedAssetIds.includes(asset.id))

  return (
    <section className="grid gap-4 xl:grid-cols-[minmax(0,0.72fr)_minmax(0,1fr)]">
      <Card className="rounded-[28px] border-border/70 bg-card/85">
        <CardHeader>
          <CardTitle>{team.name}</CardTitle>
          <p className="text-sm text-muted-foreground">
            {team.tenantName} • {team.assignedAssetCount} assets currently assigned
          </p>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-wrap gap-2">
            <Badge variant="outline" className="rounded-full border-border/70 bg-background/60">
              {team.members.length} members
            </Badge>
            <Badge variant="outline" className="rounded-full border-border/70 bg-background/60">
              {team.tenantName}
            </Badge>
          </div>
          <div className="space-y-2">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Members</p>
            {team.members.length === 0 ? (
              <p className="text-sm text-muted-foreground">No members have been added yet.</p>
            ) : (
              <div className="space-y-2">
                {team.members.map((member) => (
                  <div key={member.userId} className="rounded-2xl border border-border/70 bg-background/45 px-3 py-2">
                    <p className="text-sm font-medium">{member.displayName}</p>
                    <p className="text-xs text-muted-foreground">{member.email}</p>
                  </div>
                ))}
              </div>
            )}
          </div>
        </CardContent>
      </Card>

      <Card className="rounded-[28px] border-border/70 bg-card/85">
        <CardHeader className="space-y-4">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle>Assign Assets</CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">
                Filter tenant assets, select a set, and bulk-assign them to this assignment group.
              </p>
            </div>
            <Badge variant="outline" className="rounded-full border-primary/30 bg-primary/10 text-primary">
              {selectedAssetIds.length} selected
            </Badge>
          </div>
          <div className="grid gap-3 md:grid-cols-[minmax(0,1.5fr)_minmax(0,1fr)_minmax(0,1fr)]">
            <Input
              placeholder="Search by asset name or external ID"
              value={filters.search}
              onChange={(event) => onFilterChange({ ...filters, search: event.target.value })}
            />
            <select
              className="rounded-md border border-input bg-background px-3 py-2 text-sm"
              value={filters.assetType}
              onChange={(event) => onFilterChange({ ...filters, assetType: event.target.value })}
            >
              <option value="">All asset types</option>
              <option value="Device">Device</option>
              <option value="Software">Software</option>
              <option value="CloudResource">Cloud Resource</option>
            </select>
            <select
              className="rounded-md border border-input bg-background px-3 py-2 text-sm"
              value={filters.criticality}
              onChange={(event) => onFilterChange({ ...filters, criticality: event.target.value })}
            >
              <option value="">All criticalities</option>
              <option value="Low">Low</option>
              <option value="Medium">Medium</option>
              <option value="High">High</option>
              <option value="Critical">Critical</option>
            </select>
          </div>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-xs text-muted-foreground">{totalAssetCount} matching assets</p>
            <div className="flex flex-wrap gap-2">
              <Button type="button" variant="outline" className="rounded-full" onClick={onToggleAllVisible} disabled={assets.length === 0}>
                {allVisibleSelected ? 'Clear visible selection' : 'Select visible'}
              </Button>
              <Button type="button" className="rounded-full" onClick={onAssignSelected} disabled={selectedAssetIds.length === 0 || isAssigningAssets}>
                {isAssigningAssets ? 'Assigning...' : 'Assign selected assets'}
              </Button>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          {isLoadingAssets ? (
            <p className="text-sm text-muted-foreground">Loading tenant assets...</p>
          ) : assets.length === 0 ? (
            <p className="text-sm text-muted-foreground">No assets matched the current filters.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full min-w-[720px] border-collapse text-sm">
                <thead>
                  <tr className="border-b border-border text-left text-muted-foreground">
                    <th className="py-2 pr-2">Select</th>
                    <th className="py-2 pr-2">Name</th>
                    <th className="py-2 pr-2">Type</th>
                    <th className="py-2 pr-2">Criticality</th>
                    <th className="py-2 pr-2">Current Owner</th>
                  </tr>
                </thead>
                <tbody>
                  {assets.map((asset) => (
                    <tr key={asset.id} className="border-b border-border/60">
                      <td className="py-2 pr-2">
                        <input
                          type="checkbox"
                          checked={selectedAssetIds.includes(asset.id)}
                          onChange={() => onToggleAsset(asset.id)}
                        />
                      </td>
                      <td className="py-2 pr-2">
                        <div>
                          <p className="font-medium">{asset.name}</p>
                          <p className="text-xs text-muted-foreground">{asset.externalId}</p>
                        </div>
                      </td>
                      <td className="py-2 pr-2">{asset.assetType}</td>
                      <td className="py-2 pr-2">{asset.criticality}</td>
                      <td className="py-2 pr-2">
                        {asset.ownerType === 'Team' ? 'Assignment Group' : asset.ownerType}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>
    </section>
  )
}
