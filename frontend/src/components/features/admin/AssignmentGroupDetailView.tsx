import { CircleQuestionMark, Plus, UserMinus } from 'lucide-react'
import type { Asset } from '@/api/assets.schemas'
import type { FilterGroup } from '@/api/asset-rules.schemas'
import type { TeamDetail } from '@/api/teams.schemas'
import type { TeamMembershipRulePreview } from '@/api/teams.schemas'
import type { UserListItem } from '@/api/users.schemas'
import { Link } from '@tanstack/react-router'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { InsetPanel } from '@/components/ui/inset-panel'
import { PaginationControls } from '@/components/ui/pagination-controls'
import {
  Popover,
  PopoverContent,
  PopoverDescription,
  PopoverHeader,
  PopoverTitle,
  PopoverTrigger,
} from '@/components/ui/popover'
import { UserRuleBuilder } from './UserRuleBuilder'

type AssignmentGroupDetailViewProps = {
  team: TeamDetail
  canManageGroup: boolean
  availableMembers: UserListItem[]
  selectedMemberId: string
  memberSearch: string
  ruleFilter: FilterGroup
  isDynamic: boolean
  rulePreview: TeamMembershipRulePreview | null
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
  isUpdatingMembers: boolean
  isPreviewingRule: boolean
  isSavingRule: boolean
  onMemberSearchChange: (value: string) => void
  onSelectedMemberChange: (userId: string) => void
  onAddMember: () => void
  onRemoveMember: (userId: string) => void
  onRuleFilterChange: (value: FilterGroup) => void
  onDynamicChange: (value: boolean) => void
  onPreviewRule: () => void
  onSaveRule: () => void
  onFilterChange: (next: { search: string; assetType: string; criticality: string }) => void
  onAssetPageChange: (page: number) => void
  onAssetPageSizeChange: (pageSize: number) => void
  onToggleAsset: (assetId: string) => void
  onToggleAllVisible: () => void
  onAssignSelected: () => void
}

export function AssignmentGroupDetailView({
  team,
  canManageGroup,
  availableMembers,
  selectedMemberId,
  memberSearch,
  ruleFilter,
  isDynamic,
  rulePreview,
  assets,
  totalAssetCount,
  assetPage,
  assetPageSize,
  assetTotalPages,
  selectedAssetIds,
  filters,
  isLoadingAssets,
  isAssigningAssets,
  isUpdatingMembers,
  isPreviewingRule,
  isSavingRule,
  onMemberSearchChange,
  onSelectedMemberChange,
  onAddMember,
  onRemoveMember,
  onRuleFilterChange,
  onDynamicChange,
  onPreviewRule,
  onSaveRule,
  onFilterChange,
  onAssetPageChange,
  onAssetPageSizeChange,
  onToggleAsset,
  onToggleAllVisible,
  onAssignSelected,
}: AssignmentGroupDetailViewProps) {
  const allVisibleSelected = assets.length > 0 && assets.every((asset) => selectedAssetIds.includes(asset.id))
  const selectedCandidate = availableMembers.find((member) => member.id === selectedMemberId) ?? null
  const canManageMembersManually = canManageGroup && !team.isDynamic

  return (
    <section className="space-y-4">
      <div className="grid gap-4 xl:grid-cols-[minmax(0,0.72fr)_minmax(0,1fr)]">
        <Card className="rounded-2xl border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_8%,transparent),transparent_58%),var(--color-card)]">
          <CardHeader>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="flex flex-wrap items-center gap-2">
                  <CardTitle className="text-2xl font-semibold tracking-[-0.04em]">{team.name}</CardTitle>
                  {team.isDefault ? (
                    <Badge className="rounded-full border border-amber-300/60 bg-amber-500/10 text-amber-700 hover:bg-amber-500/10 dark:text-amber-300">
                      Fallback
                    </Badge>
                  ) : null}
                  {team.isDynamic ? (
                    <Badge className="rounded-full border border-primary/20 bg-primary/10 text-primary hover:bg-primary/10">
                      Dynamic
                    </Badge>
                  ) : null}
                </div>
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
              <SummaryMetric
                label="Current Risk"
                value={formatRiskScore(team.currentRiskScore)}
                info={team.riskExplanation ? <TeamRiskExplanationPopover team={team} /> : null}
              />
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
            <div className="flex flex-wrap items-center justify-between gap-3">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Members</p>
              {canManageGroup ? null : (
                <Badge variant="outline" className="rounded-full border-border/70 bg-background/50 text-muted-foreground">
                  Read-only
                </Badge>
              )}
            </div>
            {canManageMembersManually ? (
              <InsetPanel emphasis="subtle" className="space-y-3 px-3 py-3">
                <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_220px_auto]">
                  <Input
                    placeholder="Find tenant user by name or email"
                    value={memberSearch}
                    onChange={(event) => onMemberSearchChange(event.target.value)}
                  />
                  <select
                    className="rounded-md border border-input bg-background px-3 py-2 text-sm"
                    value={selectedMemberId}
                    onChange={(event) => onSelectedMemberChange(event.target.value)}
                  >
                    <option value="">Select user</option>
                    {availableMembers.map((member) => (
                      <option key={member.id} value={member.id}>
                        {member.displayName} · {member.email}
                      </option>
                    ))}
                  </select>
                  <Button
                    type="button"
                    className="rounded-full"
                    onClick={onAddMember}
                    disabled={!selectedCandidate || isUpdatingMembers}
                  >
                    <Plus className="mr-2 size-4" />
                    {isUpdatingMembers ? 'Updating...' : 'Add member'}
                  </Button>
                </div>
                <p className="text-xs text-muted-foreground">
                  Add tenant users to this assignment group so they can take ownership of related work.
                </p>
              </InsetPanel>
            ) : (
              <p className="text-sm text-muted-foreground">
                {team.isDynamic
                  ? 'This group is dynamic. Membership is controlled by rules and matched users are added or removed automatically.'
                  : 'Membership is visible to all roles. Only Global Admin can add or remove group members.'}
              </p>
            )}
            {team.members.length === 0 ? (
              <p className="text-sm text-muted-foreground">No members have been added yet.</p>
            ) : (
              <div className="space-y-2">
                {team.members.map((member) => (
                  <InsetPanel key={member.userId} emphasis="subtle" className="px-3 py-2">
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <p className="text-sm font-medium">{member.displayName}</p>
                        <p className="text-xs text-muted-foreground">{member.email}</p>
                      </div>
                      {canManageMembersManually ? (
                        <Button
                          type="button"
                          variant="outline"
                          size="sm"
                          className="rounded-full"
                          onClick={() => onRemoveMember(member.userId)}
                          disabled={isUpdatingMembers}
                        >
                          <UserMinus className="mr-2 size-4" />
                          Remove
                        </Button>
                      ) : null}
                    </div>
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
          <div className="space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Membership Rule</p>
                <p className="mt-1 text-sm text-muted-foreground">
                  Dynamic groups automatically sync members from user profile rules.
                </p>
              </div>
              {canManageGroup ? null : (
                <Badge variant="outline" className="rounded-full border-border/70 bg-background/50 text-muted-foreground">
                  Read-only
                </Badge>
              )}
            </div>
            <InsetPanel emphasis="subtle" className="space-y-4 px-4 py-4">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="space-y-1">
                  <p className="text-sm font-medium">Automatic member assignment</p>
                  <p className="text-xs text-muted-foreground">
                    Matching users are added to the group on rule save and on future sign-ins.
                  </p>
                </div>
                <div className="flex items-center gap-3">
                  <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                    {isDynamic ? 'Dynamic' : 'Static'}
                  </span>
                  <button
                    type="button"
                    role="switch"
                    aria-checked={isDynamic}
                    onClick={() => onDynamicChange(!isDynamic)}
                    disabled={!canManageGroup}
                    className={`relative h-7 w-12 rounded-full border transition ${
                      isDynamic
                        ? 'border-primary/40 bg-primary/20'
                        : 'border-border/70 bg-background/60'
                    } disabled:cursor-not-allowed disabled:opacity-50`}
                  >
                    <span
                      className={`absolute top-0.5 size-5 rounded-full bg-white shadow transition ${
                        isDynamic ? 'left-6' : 'left-0.5'
                      }`}
                    />
                  </button>
                </div>
              </div>
              {team.isDynamic || isDynamic ? (
                <UserRuleBuilder value={ruleFilter} onChange={onRuleFilterChange} readOnly={!canManageGroup} />
              ) : (
                <InsetPanel emphasis="subtle" className="px-3 py-3 text-sm text-muted-foreground">
                  Turn on dynamic membership to define rule-based group membership.
                </InsetPanel>
              )}
              <div className="flex flex-wrap items-center gap-2">
                <Button type="button" variant="outline" className="rounded-full" onClick={onPreviewRule} disabled={isPreviewingRule}>
                  {isPreviewingRule ? 'Previewing...' : 'Preview matches'}
                </Button>
                <Button type="button" className="rounded-full" onClick={onSaveRule} disabled={!canManageGroup || isSavingRule}>
                  {isSavingRule ? 'Saving...' : isDynamic ? 'Save dynamic rule' : 'Save as static group'}
                </Button>
              </div>
              {rulePreview ? (
                <InsetPanel emphasis="subtle" className="space-y-2 px-3 py-3">
                  <p className="text-sm font-medium">{rulePreview.count} matching users</p>
                  {rulePreview.samples.length === 0 ? (
                    <p className="text-xs text-muted-foreground">No current tenant users match this rule yet.</p>
                  ) : (
                    <div className="space-y-2">
                      {rulePreview.samples.map((sample) => (
                        <div key={sample.userId} className="flex items-start justify-between gap-3 text-sm">
                          <div>
                            <p className="font-medium">{sample.displayName}</p>
                            <p className="text-xs text-muted-foreground">{sample.email}</p>
                          </div>
                          {sample.company ? (
                            <Badge variant="outline" className="rounded-full border-border/70 bg-background/50">
                              {sample.company}
                            </Badge>
                          ) : null}
                        </div>
                      ))}
                    </div>
                  )}
                </InsetPanel>
              ) : null}
              {team.membershipRule?.lastExecutedAt ? (
                <p className="text-xs text-muted-foreground">
                  Last applied at {new Date(team.membershipRule.lastExecutedAt).toLocaleString()} for {team.membershipRule.lastMatchCount ?? 0} matching users.
                </p>
              ) : null}
            </InsetPanel>
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
            <div className="flex flex-wrap gap-2">
              {canManageGroup ? null : (
                <Badge variant="outline" className="rounded-full border-border/70 bg-background/50 text-muted-foreground">
                  Read-only
                </Badge>
              )}
              <Badge variant="outline" className="rounded-full border-primary/30 bg-primary/10 text-primary">
                {selectedAssetIds.length} selected
              </Badge>
            </div>
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
              <Button type="button" variant="outline" className="rounded-full" onClick={onToggleAllVisible} disabled={!canManageGroup || assets.length === 0}>
                {allVisibleSelected ? 'Clear visible selection' : 'Select visible'}
              </Button>
              <Button type="button" className="rounded-full" onClick={onAssignSelected} disabled={!canManageGroup || selectedAssetIds.length === 0 || isAssigningAssets}>
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
                          disabled={!canManageGroup}
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

function SummaryMetric({ label, value, info }: { label: string; value: string; info?: React.ReactNode }) {
  return (
    <InsetPanel className="p-4">
      <div className="flex items-center gap-1.5">
        <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
        {info}
      </div>
      <p className="mt-2 text-2xl font-semibold tracking-tight">{value}</p>
    </InsetPanel>
  )
}

function TeamRiskExplanationPopover({ team }: { team: TeamDetail }) {
  const explanation = team.riskExplanation
  if (!explanation) {
    return null
  }

  return (
    <Popover>
      <PopoverTrigger className="inline-flex items-center rounded-full text-muted-foreground/80 transition-colors hover:text-foreground focus-visible:outline-none focus-visible:text-foreground">
        <CircleQuestionMark className="size-4" />
      </PopoverTrigger>
      <PopoverContent side="right" align="start" sideOffset={10} className="w-[28rem] gap-3 rounded-2xl p-4">
        <PopoverHeader>
          <PopoverTitle>Team risk breakdown</PopoverTitle>
          <PopoverDescription>
            This score rolls up the highest-risk assets owned by the team, plus the current severity mix across open episodes.
          </PopoverDescription>
        </PopoverHeader>
        <div className="grid gap-3 sm:grid-cols-2">
          <SummaryMetric label="Score" value={explanation.score.toFixed(0)} />
          <SummaryMetric label="Formula version" value={explanation.calculationVersion} />
          <SummaryMetric label="Assets" value={String(explanation.assetCount)} />
          <SummaryMetric label="Open episodes" value={String(explanation.openEpisodeCount)} />
          <SummaryMetric label="Max asset risk" value={explanation.maxAssetRiskScore.toFixed(0)} />
          <SummaryMetric label="Top 3 average" value={explanation.topThreeAverage.toFixed(2)} />
        </div>
        <div className="rounded-2xl border border-border/70 bg-background/60 p-3">
          <p className="text-xs font-medium text-muted-foreground">Formula</p>
          <p className="mt-2 text-sm leading-relaxed text-foreground/90">
            Weighted top-risk asset + weighted top-three average + severity bonuses.
          </p>
          <div className="mt-3 grid gap-2 sm:grid-cols-2">
            <SummaryMetric label="Max asset contribution" value={explanation.maxAssetContribution.toFixed(2)} />
            <SummaryMetric label="Top 3 contribution" value={explanation.topThreeContribution.toFixed(2)} />
            <SummaryMetric label={`Critical (${explanation.criticalEpisodeCount})`} value={explanation.criticalContribution.toFixed(2)} />
            <SummaryMetric label={`High (${explanation.highEpisodeCount})`} value={explanation.highContribution.toFixed(2)} />
            <SummaryMetric label={`Medium (${explanation.mediumEpisodeCount})`} value={explanation.mediumContribution.toFixed(2)} />
            <SummaryMetric label={`Low (${explanation.lowEpisodeCount})`} value={explanation.lowContribution.toFixed(2)} />
          </div>
        </div>
        <div className="space-y-2">
          <p className="text-xs font-medium text-muted-foreground">Persisted factors</p>
          {explanation.factors.map((factor) => (
            <InsetPanel key={factor.name} emphasis="subtle" className="px-3 py-2.5">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="text-sm font-medium text-foreground">{factor.name}</p>
                  <p className="mt-1 text-xs leading-relaxed text-muted-foreground">{factor.description}</p>
                </div>
                <span className="text-sm font-semibold tabular-nums text-foreground">{factor.impact.toFixed(2)}</span>
              </div>
            </InsetPanel>
          ))}
        </div>
      </PopoverContent>
    </Popover>
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
