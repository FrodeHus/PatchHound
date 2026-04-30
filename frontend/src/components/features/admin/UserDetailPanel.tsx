import { useState } from 'react'
import type { TenantListItem } from '@/api/settings.schemas'
import type { TeamItem } from '@/api/teams.schemas'
import type { UserAuditItem, UserDetail } from '@/api/users.schemas'
import type { CurrentUser } from '@/server/auth.functions'
import { AuditTimeline, type AuditTimelineEvent } from '@/components/features/audit/AuditTimeline'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Separator } from '@/components/ui/separator'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'

const globalRoleOptions = [
  'GlobalAdmin',
  'SecurityManager',
  'SecurityAnalyst',
  'TechnicalManager',
  'AssetOwner',
  'Stakeholder',
  'Auditor',
] as const

const customerRoleOptions = [
  'CustomerAdmin',
  'CustomerOperator',
  'CustomerViewer',
] as const

type AccessPayload = {
  displayName: string
  email: string
  company: string | null
  isEnabled: boolean
  accessScope: string
  roles: string[]
  tenantAccess: Array<{ tenantId: string; roles: string[] }>
}

type GroupsPayload = {
  teamIds: string[]
}

type UserDetailPanelProps = {
  user: UserDetail | undefined
  currentUser: CurrentUser
  teams: TeamItem[]
  tenants: TenantListItem[]
  auditItems: UserAuditItem[]
  auditFilters: {
    entityType: string
    action: string
  }
  isLoading: boolean
  isSaving: boolean
  onAuditFilterChange: (next: { entityType: string; action: string }) => void
  onSaveAccess: (payload: AccessPayload) => void
  onSaveGroups: (payload: GroupsPayload) => void
}

export function UserDetailPanel({
  user,
  currentUser,
  teams,
  tenants,
  auditItems,
  auditFilters,
  isLoading,
  isSaving,
  onAuditFilterChange,
  onSaveAccess,
  onSaveGroups,
}: UserDetailPanelProps) {
  if (!user) {
    return (
      <Card className="rounded-2xl border-border/70">
        <CardContent className="px-6 py-10 text-sm text-muted-foreground">
          Select a user to review profile, tenant reach, customer scope, assignment groups, and audit history.
        </CardContent>
      </Card>
    )
  }

  const userSnapshotKey = [
    user.id,
    user.displayName,
    user.email,
    user.company ?? '',
    String(user.isEnabled),
    user.accessScope,
    user.roles.join(','),
    user.tenantAccess.map((item) => `${item.tenantId}:${item.roles.join(',')}`).join('|'),
    user.teams.map((team) => team.teamId).join(','),
  ].join('|')

  return (
    <UserDetailEditor
      key={userSnapshotKey}
      user={user}
      currentUser={currentUser}
      teams={teams}
      tenants={tenants}
      auditItems={auditItems}
      auditFilters={auditFilters}
      isLoading={isLoading}
      isSaving={isSaving}
      onAuditFilterChange={onAuditFilterChange}
      onSaveAccess={onSaveAccess}
      onSaveGroups={onSaveGroups}
    />
  )
}

function UserDetailEditor({
  user,
  currentUser,
  teams,
  tenants,
  auditItems,
  auditFilters,
  isLoading,
  isSaving,
  onAuditFilterChange,
  onSaveAccess,
  onSaveGroups,
}: {
  user: UserDetail
  currentUser: CurrentUser
  teams: TeamItem[]
  tenants: TenantListItem[]
  auditItems: UserAuditItem[]
  auditFilters: {
    entityType: string
    action: string
  }
  isLoading: boolean
  isSaving: boolean
  onAuditFilterChange: (next: { entityType: string; action: string }) => void
  onSaveAccess: UserDetailPanelProps['onSaveAccess']
  onSaveGroups: UserDetailPanelProps['onSaveGroups']
}) {
  const [displayName, setDisplayName] = useState(user.displayName)
  const [email, setEmail] = useState(user.email)
  const [company, setCompany] = useState(user.company ?? '')
  const [isEnabled, setIsEnabled] = useState(user.isEnabled)
  const [accessScope, setAccessScope] = useState(user.accessScope)
  const [selectedRoles, setSelectedRoles] = useState<string[]>(user.roles)
  const [selectedTeamIds, setSelectedTeamIds] = useState<string[]>(user.teams.map((team) => team.teamId))
  const [tenantAccess, setTenantAccess] = useState<Array<{ tenantId: string; roles: string[] }>>(
    user.tenantAccess.map((item) => ({ tenantId: item.tenantId, roles: [...item.roles] })),
  )

  const roleOptions = accessScope === 'Customer' ? customerRoleOptions : globalRoleOptions
  const canManageAcrossTenants = (currentUser.activeRoles ?? []).includes('GlobalAdmin')
  const availableScopeOptions = canManageAcrossTenants
    ? ['Internal', 'Customer']
    : ['Customer']
  const visibleTenants = canManageAcrossTenants
    ? tenants
    : tenants.filter((tenant) => tenant.id === user.currentTenantId)

  const auditEvents: AuditTimelineEvent[] = auditItems.map((item) => ({
    id: item.id,
    title: item.summary ?? `${item.entityType} ${item.action.toLowerCase()}`,
    action: item.action,
    timestamp: item.timestamp,
    actorName: item.userDisplayName ?? 'System',
    badges: [
      { label: item.entityType, tone: 'neutral' },
      { label: item.action, tone: 'info' },
    ],
  }))

  function toggleTenant(tenantId: string) {
    setTenantAccess((current) =>
      current.some((item) => item.tenantId === tenantId)
        ? current.filter((item) => item.tenantId !== tenantId)
        : [...current, { tenantId, roles: accessScope === 'Customer' ? ['CustomerViewer'] : ['Stakeholder'] }],
    )
  }

  function toggleTenantRole(tenantId: string, role: string) {
    setTenantAccess((current) =>
      current.map((item) =>
        item.tenantId !== tenantId
          ? item
          : {
              ...item,
              roles: item.roles.includes(role)
                ? item.roles.filter((existing) => existing !== role)
                : [...item.roles, role],
            },
      ),
    )
  }

  const initials = user.displayName
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('')

  const assignedTenants = tenantAccess.length
  const assignedGroups = selectedTeamIds.length
  const assignedRoles = selectedRoles.length

  const saveAccess = () => onSaveAccess({
    displayName: displayName.trim(),
    email: email.trim(),
    company: company.trim().length > 0 ? company.trim() : null,
    isEnabled,
    accessScope,
    roles: selectedRoles,
    tenantAccess: tenantAccess.map((item) => ({
      tenantId: item.tenantId,
      roles: item.roles,
    })),
  })

  const saveGroups = () => onSaveGroups({ teamIds: selectedTeamIds })

  return (
    <Card className="rounded-2xl border-border/70 bg-card/80">
      <CardHeader className="space-y-4">
        <div className="flex items-start gap-4">
          <Avatar className="size-14 rounded-2xl border border-border/70 bg-primary/10 text-primary">
            <AvatarFallback className="rounded-2xl bg-primary/10 text-lg font-semibold text-primary">
              {initials}
            </AvatarFallback>
          </Avatar>
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <CardTitle className="truncate text-2xl font-semibold tracking-[-0.04em]">
                  {user.displayName}
                </CardTitle>
                <CardDescription className="mt-1 truncate">
                  {user.email}
                </CardDescription>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">
                  {accessScope}
                </Badge>
                <Badge
                  variant="outline"
                  className={isEnabled
                    ? 'rounded-full border-emerald-300/60 bg-emerald-500/10 text-emerald-700'
                    : 'rounded-full border-rose-300/60 bg-rose-500/10 text-rose-700'}
                >
                  {isEnabled ? 'Enabled' : 'Disabled'}
                </Badge>
              </div>
            </div>
          </div>
        </div>
        <div className="grid gap-2 md:grid-cols-3">
          <StatTile label="Tenant reach" value={`${assignedTenants} tenant${assignedTenants === 1 ? '' : 's'}`} />
          <StatTile label="Assignment groups" value={`${assignedGroups} assignment group${assignedGroups === 1 ? '' : 's'}`} />
          <StatTile label="Current roles" value={`${assignedRoles} role${assignedRoles === 1 ? '' : 's'}`} />
        </div>
      </CardHeader>
      <CardContent>
        <Tabs defaultValue="overview" className="space-y-4">
          <TabsList className="grid w-full grid-cols-4">
            <TabsTrigger value="overview">Overview</TabsTrigger>
            <TabsTrigger value="access">Access</TabsTrigger>
            <TabsTrigger value="groups">Groups</TabsTrigger>
            <TabsTrigger value="audit">Audit</TabsTrigger>
          </TabsList>

          <TabsContent value="overview" className="space-y-4">
            <div className="rounded-xl border border-border/70 bg-background/50 p-4">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Basic info</p>
                  <p className="mt-1 text-sm text-muted-foreground">
                    Read-only orientation before editing access.
                  </p>
                </div>
                {user.currentTenantName ? (
                  <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">
                    Viewing {user.currentTenantName}
                  </Badge>
                ) : null}
              </div>
              <Separator className="my-4" />
              <div className="grid gap-4 md:grid-cols-2">
                <DetailRow label="Display name" value={user.displayName} />
                <DetailRow label="Email / UPN" value={user.email} />
                <DetailRow label="Company" value={user.company || 'No company set'} />
                <DetailRow label="Entra object ID" value={user.entraObjectId} />
                <DetailRow label="Access scope" value={user.accessScope} />
                <DetailRow label="Account status" value={user.isEnabled ? 'Enabled' : 'Disabled'} />
              </div>
            </div>

            <div className="grid gap-3 md:grid-cols-3">
              <SummaryPanel
                title="Tenant access"
                value={`${user.tenantAccess.length} tenant${user.tenantAccess.length === 1 ? '' : 's'}`}
                description={user.tenantAccess.length > 0
                  ? user.tenantAccess
                      .map((item) => tenants.find((tenant) => tenant.id === item.tenantId)?.name)
                      .filter((name): name is string => Boolean(name))
                      .join(', ')
                  : 'No tenant access assigned'}
              />
              <SummaryPanel
                title="Groups"
                value={`${user.teams.length} assignment group${user.teams.length === 1 ? '' : 's'}`}
                description={user.teams.length > 0
                  ? user.teams.map((team) => team.teamName).join(', ')
                  : 'No groups assigned'}
              />
              <SummaryPanel
                title="Current tenant roles"
                value={`${user.roles.length} role${user.roles.length === 1 ? '' : 's'}`}
                description={user.roles.length > 0 ? user.roles.join(', ') : 'No roles assigned'}
              />
            </div>
          </TabsContent>

          <TabsContent value="access" className="space-y-4">
            <div className="grid gap-4 md:grid-cols-2">
              <Field label="Name">
                <Input value={displayName} onChange={(event) => setDisplayName(event.target.value)} />
              </Field>
              <Field label="Email / UPN">
                <Input value={email} onChange={(event) => setEmail(event.target.value)} />
              </Field>
              <Field label="Company">
                <Input value={company} onChange={(event) => setCompany(event.target.value)} />
              </Field>
              <Field label="Entra object ID">
                <Input value={user.entraObjectId} readOnly className="bg-muted/50" />
              </Field>
            </div>

            <div className="grid gap-4 lg:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)]">
              <div className="rounded-xl border border-border/70 bg-background/50 p-4">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Access status</p>
                <div className="mt-3 flex flex-wrap gap-2">
                  <Button type="button" variant={isEnabled ? 'default' : 'outline'} className="rounded-full" onClick={() => setIsEnabled(true)}>
                    Enabled
                  </Button>
                  <Button type="button" variant={!isEnabled ? 'destructive' : 'outline'} className="rounded-full" onClick={() => setIsEnabled(false)}>
                    Disabled
                  </Button>
                </div>

                <p className="mt-5 text-xs uppercase tracking-[0.18em] text-muted-foreground">Identity scope</p>
                <p className="mt-1 text-sm text-muted-foreground">
                  Internal users can operate across the MSSP tenant portfolio. Customer users are restricted to the tenants assigned below.
                </p>
                <div className="mt-3 flex flex-wrap gap-2">
                  {availableScopeOptions.includes('Internal') ? (
                    <Button type="button" variant={accessScope === 'Internal' ? 'default' : 'outline'} className="rounded-full" onClick={() => setAccessScope('Internal')}>
                      Internal MSSP
                    </Button>
                  ) : null}
                  <Button type="button" variant={accessScope === 'Customer' ? 'default' : 'outline'} className="rounded-full" onClick={() => setAccessScope('Customer')}>
                    Customer
                  </Button>
                </div>
              </div>

              <div className="rounded-xl border border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_10%,var(--background)),var(--background)_62%)] p-4">
                <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Access summary</p>
                <div className="mt-3 flex flex-wrap gap-2">
                  <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">
                    {tenantAccess.length} tenant{tenantAccess.length === 1 ? '' : 's'}
                  </Badge>
                  <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">
                    {selectedTeamIds.length} local groups
                  </Badge>
                  <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">
                    {selectedRoles.length} current-tenant role{selectedRoles.length === 1 ? '' : 's'}
                  </Badge>
                </div>
                <p className="mt-4 text-sm leading-6 text-muted-foreground">
                  The tenant access matrix below is the security source of truth. Default customer groups can be derived automatically from those assignments.
                </p>
              </div>
            </div>

            <SelectionSection
              title="Current tenant roles"
              description="These are the roles active for the tenant currently selected in the session. They control what the user can do when operating inside this tenant."
              items={roleOptions.map((role) => ({ id: role, label: role }))}
              selectedIds={selectedRoles}
              onToggle={(role) => {
                setSelectedRoles((current) =>
                  current.includes(role)
                    ? current.filter((item) => item !== role)
                    : [...current, role],
                )
              }}
            />

            <div className="rounded-xl border border-border/70 bg-background/50 p-4">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Tenant access matrix</p>
                  <p className="mt-1 text-sm text-muted-foreground">
                    Assign exactly which tenants this identity may enter. For customer users, this defines the full trust boundary.
                  </p>
                </div>
                {canManageAcrossTenants ? (
                  <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">
                    MSSP control plane
                  </Badge>
                ) : null}
              </div>

              <div className="mt-4 space-y-3">
                {visibleTenants.map((tenant) => {
                  const assignment = tenantAccess.find((item) => item.tenantId === tenant.id)
                  const isAssigned = Boolean(assignment)
                  const tenantRoleChoices = accessScope === 'Customer' ? customerRoleOptions : globalRoleOptions

                  return (
                    <div
                      key={tenant.id}
                      className={`rounded-2xl border p-4 transition ${
                        isAssigned
                          ? 'border-primary/30 bg-primary/5'
                          : 'border-border/70 bg-background/60'
                      }`}
                    >
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <div>
                          <p className="font-medium tracking-tight">{tenant.name}</p>
                          <p className="mt-1 text-sm text-muted-foreground">{tenant.entraTenantId}</p>
                        </div>
                        <Button
                          type="button"
                          variant={isAssigned ? 'default' : 'outline'}
                          className="rounded-full"
                          onClick={() => toggleTenant(tenant.id)}
                        >
                          {isAssigned ? 'Assigned' : 'Grant access'}
                        </Button>
                      </div>

                      {isAssigned ? (
                        <div className="mt-4">
                          <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Tenant roles</p>
                          <div className="mt-2 flex flex-wrap gap-2">
                            {tenantRoleChoices.map((role) => {
                              const active = assignment?.roles.includes(role) ?? false
                              return (
                                <button
                                  key={`${tenant.id}-${role}`}
                                  type="button"
                                  onClick={() => toggleTenantRole(tenant.id, role)}
                                  className={`rounded-full border px-3 py-1.5 text-sm transition ${
                                    active
                                      ? 'border-primary/40 bg-primary/10 text-primary'
                                      : 'border-border/70 bg-background text-foreground/80'
                                  }`}
                                >
                                  {role}
                                </button>
                              )
                            })}
                          </div>
                        </div>
                      ) : null}
                    </div>
                  )
                })}
              </div>
            </div>

            <div className="flex justify-end">
              <Button
                type="button"
                className="rounded-full"
                disabled={isSaving || isLoading}
                onClick={saveAccess}
              >
                {isSaving ? 'Saving access...' : 'Save access model'}
              </Button>
            </div>
          </TabsContent>

          <TabsContent value="groups" className="space-y-4">
            <SelectionSection
              title="Assignment groups"
              description="These memberships affect routing and local task ownership in the currently selected tenant."
              items={teams.map((team) => ({
                id: team.id,
                label: team.name,
                pill: team.isDefault ? 'Fallback' : null,
              }))}
              selectedIds={selectedTeamIds}
              onToggle={(teamId) => {
                setSelectedTeamIds((current) =>
                  current.includes(teamId)
                    ? current.filter((item) => item !== teamId)
                    : [...current, teamId],
                )
              }}
            />
            <div className="flex justify-end">
              <Button
                type="button"
                className="rounded-full"
                disabled={isSaving || isLoading}
                onClick={saveGroups}
              >
                {isSaving ? 'Saving groups...' : 'Save group memberships'}
              </Button>
            </div>
          </TabsContent>

          <TabsContent value="audit" className="space-y-4">
            <div className="grid gap-3 md:grid-cols-2">
              <Select
                value={auditFilters.entityType || 'all'}
                onValueChange={(value) => onAuditFilterChange({ ...auditFilters, entityType: value === 'all' ? '' : (value ?? '') })}
              >
                <SelectTrigger
                  aria-label="Filter audit by entity type"
                  className="h-8 w-full justify-between rounded-lg border-border/70 bg-background/70"
                >
                  <SelectValue placeholder="All entity types" />
                </SelectTrigger>
                <SelectContent align="start" className="rounded-xl border-border/70">
                  <SelectItem value="all">All entity types</SelectItem>
                  <SelectItem value="User">User</SelectItem>
                  <SelectItem value="UserTenantRole">Role assignment</SelectItem>
                  <SelectItem value="TeamMember">Group membership</SelectItem>
                </SelectContent>
              </Select>
              <Select
                value={auditFilters.action || 'all'}
                onValueChange={(value) => onAuditFilterChange({ ...auditFilters, action: value === 'all' ? '' : (value ?? '') })}
              >
                <SelectTrigger
                  aria-label="Filter audit by action"
                  className="h-8 w-full justify-between rounded-lg border-border/70 bg-background/70"
                >
                  <SelectValue placeholder="All actions" />
                </SelectTrigger>
                <SelectContent align="start" className="rounded-xl border-border/70">
                  <SelectItem value="all">All actions</SelectItem>
                  <SelectItem value="Created">Created</SelectItem>
                  <SelectItem value="Updated">Updated</SelectItem>
                  <SelectItem value="Deleted">Deleted</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <AuditTimeline events={auditEvents} emptyMessage="No audit entries matched the current filters." />
          </TabsContent>
        </Tabs>
      </CardContent>
    </Card>
  )
}

function StatTile({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-border/70 bg-background/55 px-3 py-2">
      <p className="text-[10px] font-medium uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
      <p className="mt-1 text-sm font-semibold tracking-tight">{value}</p>
    </div>
  )
}

function SummaryPanel({
  title,
  value,
  description,
}: {
  title: string
  value: string
  description: string
}) {
  return (
    <div className="rounded-xl border border-border/70 bg-background/50 p-4">
      <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{title}</p>
      <p className="mt-2 text-lg font-semibold tracking-tight">{value}</p>
      <p className="mt-1 line-clamp-3 text-sm leading-6 text-muted-foreground">{description}</p>
    </div>
  )
}

function DetailRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0">
      <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{label}</p>
      <p className="mt-1 break-words text-sm font-medium tracking-tight">{value}</p>
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="space-y-2">
      <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{label}</span>
      {children}
    </label>
  )
}

function SelectionSection({
  title,
  description,
  items,
  selectedIds,
  onToggle,
}: {
  title: string
  description: string
  items: Array<{ id: string; label: string; pill?: string | null }>
  selectedIds: string[]
  onToggle: (id: string) => void
}) {
  return (
    <div className="rounded-xl border border-border/70 bg-background/50 p-4">
      <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">{title}</p>
      <p className="mt-1 text-sm text-muted-foreground">{description}</p>
      <div className="mt-3 flex flex-wrap gap-2">
        {items.map((item) => {
          const active = selectedIds.includes(item.id)
          return (
            <button
              key={item.id}
              type="button"
              onClick={() => onToggle(item.id)}
              className={`inline-flex items-center gap-2 rounded-full border px-3 py-1.5 text-sm transition ${
                active
                  ? 'border-primary/40 bg-primary/10 text-primary'
                  : 'border-border/70 bg-background text-foreground/80'
              }`}
            >
              <span>{item.label}</span>
              {item.pill ? (
                <span className="rounded-full border border-amber-300/60 bg-amber-500/10 px-1.5 py-0.5 text-[10px] uppercase tracking-[0.14em] text-amber-700">
                  {item.pill}
                </span>
              ) : null}
            </button>
          )
        })}
      </div>
    </div>
  )
}
