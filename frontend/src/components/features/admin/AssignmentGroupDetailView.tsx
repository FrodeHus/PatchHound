import { Plus, UserMinus } from 'lucide-react'
import type { FilterGroup } from '@/api/asset-rules.schemas'
import type { TeamDetail, TeamMembershipRulePreview } from '@/api/teams.schemas'
import type { UserListItem } from '@/api/users.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { InsetPanel } from '@/components/ui/inset-panel'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
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
}: AssignmentGroupDetailViewProps) {
  const selectedCandidate = availableMembers.find((member) => member.id === selectedMemberId) ?? null
  const canManageMembersManually = canManageGroup && !team.isDynamic

  return (
    <section className="space-y-4">
      <Card className="rounded-[2rem] border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_8%,transparent),transparent_60%),var(--color-card)] shadow-sm">
        <CardHeader className="space-y-4">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div className="space-y-2">
              <div className="flex flex-wrap items-center gap-2">
                <CardTitle className="text-3xl font-semibold tracking-[-0.04em]">{team.name}</CardTitle>
                {team.isDefault ? (
                  <Badge className="rounded-full border border-amber-300/60 bg-amber-500/10 text-amber-700 hover:bg-amber-500/10 dark:text-amber-300">
                    Fallback
                  </Badge>
                ) : null}
                {team.isDynamic ? (
                  <Badge className="rounded-full border border-primary/20 bg-primary/10 text-primary hover:bg-primary/10">
                    Dynamic
                  </Badge>
                ) : (
                  <Badge variant="outline" className="rounded-full border-border/70 bg-background/50 text-muted-foreground">
                    Static
                  </Badge>
                )}
              </div>
              <p className="text-sm text-muted-foreground">
                Manage group metadata, membership, and rule-based assignment for {team.tenantName}.
              </p>
            </div>
            <div className="flex flex-wrap gap-2">
              <Badge variant="outline" className="rounded-full border-primary/20 bg-primary/10 text-primary">
                {team.tenantName}
              </Badge>
              {!canManageGroup ? (
                <Badge variant="outline" className="rounded-full border-border/70 bg-background/50 text-muted-foreground">
                  Read-only
                </Badge>
              ) : null}
            </div>
          </div>
          <div className="grid gap-3 sm:grid-cols-4">
            <OverviewMetric label="Tenant" value={team.tenantName} />
            <OverviewMetric label="Members" value={String(team.members.length)} />
            <OverviewMetric label="Mode" value={team.isDynamic ? 'Dynamic' : 'Static'} />
            <OverviewMetric label="Fallback" value={team.isDefault ? 'Yes' : 'No'} />
          </div>
        </CardHeader>
      </Card>

      <Tabs defaultValue="overview" className="space-y-4">
        <TabsList className="h-10 w-full justify-start rounded-xl bg-muted/50 p-1">
          <TabsTrigger value="overview" className="rounded-lg px-4 text-sm">Overview</TabsTrigger>
          <TabsTrigger value="members" className="rounded-lg px-4 text-sm">Members</TabsTrigger>
          <TabsTrigger value="rules" className="rounded-lg px-4 text-sm">Rules</TabsTrigger>
          <TabsTrigger value="settings" className="rounded-lg px-4 text-sm">Settings</TabsTrigger>
        </TabsList>

        <TabsContent value="overview" className="space-y-4 pt-1">
          <Card className="rounded-2xl border-border/70 bg-card/85">
            <CardHeader>
              <CardTitle>Group overview</CardTitle>
            </CardHeader>
            <CardContent className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <DetailField label="Name" value={team.name} />
              <DetailField label="Tenant" value={team.tenantName} />
              <DetailField label="Membership mode" value={team.isDynamic ? 'Rule-based sync' : 'Manual curation'} />
              <DetailField label="Fallback group" value={team.isDefault ? 'Enabled' : 'Disabled'} />
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="members" className="space-y-4 pt-1">
          <Card className="rounded-2xl border-border/70 bg-card/85">
            <CardHeader className="space-y-2">
              <CardTitle>Members</CardTitle>
              <p className="text-sm text-muted-foreground">
                {team.isDynamic
                  ? 'This group is dynamic. Membership is controlled by rules and synced automatically.'
                  : 'Add or remove tenant users who should be part of this assignment group.'}
              </p>
            </CardHeader>
            <CardContent className="space-y-4">
              {canManageMembersManually ? (
                <InsetPanel emphasis="subtle" className="space-y-3 px-4 py-4">
                  <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_260px_auto]">
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
                </InsetPanel>
              ) : null}

              {team.members.length === 0 ? (
                <p className="text-sm text-muted-foreground">No members have been added yet.</p>
              ) : (
                <div className="space-y-2">
                  {team.members.map((member) => (
                    <InsetPanel key={member.userId} emphasis="subtle" className="px-4 py-3">
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
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="rules" className="space-y-4 pt-1">
          <Card className="rounded-2xl border-border/70 bg-card/85">
            <CardHeader className="space-y-2">
              <CardTitle>Membership rules</CardTitle>
              <p className="text-sm text-muted-foreground">
                Dynamic groups automatically add matching users and remove users who no longer match.
              </p>
            </CardHeader>
            <CardContent className="space-y-4">
              <InsetPanel emphasis="subtle" className="space-y-4 px-4 py-4">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div className="space-y-1">
                    <p className="text-sm font-medium">Automatic member assignment</p>
                    <p className="text-xs text-muted-foreground">
                      Matching users are synced on rule save and on future sign-ins or profile updates.
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
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="settings" className="space-y-4 pt-1">
          <Card className="rounded-2xl border-border/70 bg-card/85">
            <CardHeader>
              <CardTitle>Settings</CardTitle>
            </CardHeader>
            <CardContent className="grid gap-4 md:grid-cols-2">
              <InsetPanel emphasis="subtle" className="px-4 py-4">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Fallback status</p>
                <p className="mt-2 text-sm text-foreground">
                  {team.isDefault
                    ? 'This group is the tenant fallback assignment group.'
                    : 'This group is not used as the tenant fallback assignment group.'}
                </p>
              </InsetPanel>
              <InsetPanel emphasis="subtle" className="px-4 py-4">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Membership model</p>
                <p className="mt-2 text-sm text-foreground">
                  {team.isDynamic
                    ? 'Members are synchronized automatically from the rule definition.'
                    : 'Members are curated manually by a Global Admin.'}
                </p>
              </InsetPanel>
            </CardContent>
          </Card>
        </TabsContent>
      </Tabs>
    </section>
  )
}

function OverviewMetric({ label, value }: { label: string; value: string }) {
  return (
    <InsetPanel className="p-4">
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
      <p className="mt-2 text-lg font-semibold tracking-tight">{value}</p>
    </InsetPanel>
  )
}

function DetailField({ label, value }: { label: string; value: string }) {
  return (
    <InsetPanel emphasis="subtle" className="px-4 py-4">
      <p className="text-[11px] uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
      <p className="mt-2 text-sm font-medium text-foreground">{value}</p>
    </InsetPanel>
  )
}
