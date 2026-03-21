import type { Asset } from '@/api/assets.schemas'
import type { TeamDetail } from '@/api/teams.schemas'
import { Link } from '@tanstack/react-router'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { InsetPanel } from '@/components/ui/inset-panel'
import { PaginationControls } from '@/components/ui/pagination-controls'

type AssignmentGroupDetailViewProps = {
  team: TeamDetail
  assets: Asset[]
  totalAssetCount: number
  assetPage: number
  assetPageSize: number
  assetTotalPages: number
  selectedAssetIds: string[]
  filters: {
    search: string
    assetType: string
    criticality: string
  }
  isLoadingAssets: boolean
  isAssigningAssets: boolean
  onFilterChange: (next: { search: string; assetType: string; criticality: string }) => void
  onAssetPageChange: (page: number) => void
  onAssetPageSizeChange: (pageSize: number) => void
  onToggleAsset: (assetId: string) => void
  onToggleAllVisible: () => void
  onAssignSelected: () => void
}

export function AssignmentGroupDetailView({
  team,
  assets,
  totalAssetCount,
  assetPage,
  assetPageSize,
  assetTotalPages,
  selectedAssetIds,
  filters,
  isLoadingAssets,
  isAssigningAssets,
  onFilterChange,
  onAssetPageChange,
  onAssetPageSizeChange,
  onToggleAsset,
  onToggleAllVisible,
  onAssignSelected,
}: AssignmentGroupDetailViewProps) {
  const allVisibleSelected = assets.length > 0 && assets.every((asset) => selectedAssetIds.includes(asset.id))

  return (
    <section className="space-y-4">
      <div className="grid gap-4 xl:grid-cols-[minmax(0,0.72fr)_minmax(0,1fr)]">
        <Card className="rounded-2xl border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_8%,transparent),transparent_58%),var(--color-card)]">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <CardTitle className="text-2xl font-semibold tracking-[-0.04em]">{team.name}</CardTitle>
                <p className="mt-1 text-sm text-muted-foreground">
                  {team.tenantName} ownership lane with {team.assignedAssetCount} assets currently assigned.
                </p>
              </div>
              <Badge variant="outline" className="rounded-full border-primary/20 bg-primary/10 text-primary">
                {team.tenantName}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-3 sm:grid-cols-3">
              <SummaryMetric label="Members" value={String(team.members.length)} />
              <SummaryMetric label="Assigned Assets" value={String(team.assignedAssetCount)} />
              <SummaryMetric label="Current Risk" value={formatRiskScore(team.currentRiskScore)} />
            </div>
          <div className="flex flex-wrap gap-2">
            <Badge variant="outline" className="rounded-full border-border/70 bg-background/50">
              {team.members.length} members
            </Badge>
            <Badge variant="outline" className="rounded-full border-border/70 bg-background/50">
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
                  <InsetPanel key={member.userId} emphasis="subtle" className="px-3 py-2">
                    <p className="text-sm font-medium">{member.displayName}</p>
                    <p className="text-xs text-muted-foreground">{member.email}</p>
                  </InsetPanel>
                ))}
              </div>
            )}
          </div>
          <div className="space-y-2">
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Top Risk Drivers</p>
            {team.topRiskAssets.length === 0 ? (
              <p className="text-sm text-muted-foreground">No scored assets are currently driving team risk.</p>
            ) : (
              <div className="space-y-2">
                {team.topRiskAssets.map((asset) => (
                  <InsetPanel key={asset.assetId} emphasis="subtle" className="px-3 py-3">
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <Link
                          to="/assets/$id"
                          params={{ id: asset.assetId }}
                          className="truncate text-sm font-medium hover:underline"
                        >
                          {asset.assetName}
                        </Link>
                        <p className="mt-1 text-xs text-muted-foreground">
                          {asset.assetType} with peak episode risk {asset.maxEpisodeRiskScore.toFixed(0)} across {asset.openEpisodeCount} open episodes.
                        </p>
                      </div>
                      <Badge variant="outline" className="rounded-full border-primary/20 bg-primary/10 text-primary">
                        {asset.currentRiskScore.toFixed(0)}
                      </Badge>
                    </div>
                  </InsetPanel>
                ))}
              </div>
            )}
          </div>
        </CardContent>
        </Card>

        <Card className="rounded-2xl border-border/70 bg-card/85">
          <CardHeader className="space-y-4">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle>Asset Assignment Workspace</CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">
                Filter tenant assets, narrow the candidate set, and assign ownership in bulk.
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
            <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{totalAssetCount} matching assets</p>
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
            <div className="space-y-4">
              <InsetPanel className="overflow-x-auto rounded-xl p-0">
                <table className="w-full min-w-[720px] border-collapse text-sm">
                <thead>
                  <tr className="border-b border-border text-left text-muted-foreground">
                    <th className="px-4 py-3 pr-2">Select</th>
                    <th className="px-4 py-3 pr-2">Name</th>
                    <th className="px-4 py-3 pr-2">Type</th>
                    <th className="px-4 py-3 pr-2">Criticality</th>
                    <th className="px-4 py-3 pr-4">Current Owner</th>
                  </tr>
                </thead>
                <tbody>
                  {assets.map((asset) => (
                    <tr key={asset.id} className="border-b border-border/60">
                      <td className="px-4 py-3 pr-2">
                        <input
                          type="checkbox"
                          checked={selectedAssetIds.includes(asset.id)}
                          onChange={() => onToggleAsset(asset.id)}
                        />
                      </td>
                      <td className="px-4 py-3 pr-2">
                        <div>
                          <p className="font-medium">{asset.name}</p>
                          <p className="text-xs text-muted-foreground">{asset.externalId}</p>
                        </div>
                      </td>
                      <td className="px-4 py-3 pr-2">
                        <Badge variant="outline" className="rounded-full border-border/70 bg-background/50">
                          {asset.assetType}
                        </Badge>
                      </td>
                      <td className="px-4 py-3 pr-2">
                        <Badge variant="outline" className="rounded-full border-border/70 bg-background/50">
                          {asset.criticality}
                        </Badge>
                      </td>
                      <td className="px-4 py-3 pr-4">
                        {formatOwnerLabel(asset.ownerType)}
                      </td>
                    </tr>
                  ))}
                </tbody>
                </table>
              </InsetPanel>
              <PaginationControls
                page={assetPage}
                pageSize={assetPageSize}
                totalCount={totalAssetCount}
                totalPages={assetTotalPages}
                onPageChange={onAssetPageChange}
                onPageSizeChange={onAssetPageSizeChange}
              />
            </div>
          )}
          </CardContent>
        </Card>
      </div>
    </section>
  )
}

function SummaryMetric({ label, value }: { label: string; value: string }) {
  return (
    <InsetPanel className="p-4">
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
      <p className="mt-2 text-2xl font-semibold tracking-tight">{value}</p>
    </InsetPanel>
  )
}

function formatOwnerLabel(ownerType: string) {
  if (ownerType === 'Team') {
    return 'Assignment Group'
  }

  if (!ownerType || ownerType === 'None') {
    return 'Unassigned'
  }

  return ownerType
}

function formatRiskScore(score: number | null) {
  if (typeof score !== 'number') {
    return '0'
  }

  return score.toFixed(0)
}
