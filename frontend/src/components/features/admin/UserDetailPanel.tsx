import { useState } from 'react'
import type { TenantListItem } from '@/api/settings.schemas'
import type { TeamItem } from '@/api/teams.schemas'
import type { UserAuditItem, UserDetail } from '@/api/users.schemas'
import type { CurrentUser } from '@/server/auth.functions'
import { AuditTimeline, type AuditTimelineEvent } from '@/components/features/audit/AuditTimeline'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
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
  onSave: (payload: {
    displayName: string
    email: string
    company: string | null
    isEnabled: boolean
    accessScope: string
    roles: string[]
    teamIds: string[]
    tenantAccess: Array<{ tenantId: string; roles: string[] }>
  }) => void
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
  onSave,
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
      onSave={onSave}
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
  onSave,
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
  onSave: UserDetailPanelProps['onSave']
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

  return (
    <Card className="rounded-2xl border-border/70">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle className="text-2xl tracking-[-0.04em]">{user.displayName}</CardTitle>
            <CardDescription className="mt-1">
              Set identity scope, assign tenant reach, and tune the roles this user carries inside each customer boundary.
            </CardDescription>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {user.currentTenantName ? (
              <Badge variant="outline" className="rounded-full border-border/70 bg-background/60">
                Viewing {user.currentTenantName}
              </Badge>
            ) : null}
            <Badge variant="outline" className="rounded-full border-border/70 bg-background/60">
              {accessScope}
            </Badge>
            <Badge
              variant="outline"
              className={user.isEnabled
                ? 'rounded-full border-emerald-300/60 bg-emerald-500/10 text-emerald-700'
                : 'rounded-full border-rose-300/60 bg-rose-500/10 text-rose-700'}
            >
              {user.isEnabled ? 'Enabled' : 'Disabled'}
            </Badge>
          </div>
        </div>
      </CardHeader>
      <CardContent>
        <Tabs defaultValue="profile" className="space-y-4">
          <TabsList className="grid w-full grid-cols-2">
            <TabsTrigger value="profile">Profile</TabsTrigger>
            <TabsTrigger value="audit">Audit</TabsTrigger>
          </TabsList>

          <TabsContent value="profile" className="space-y-4">
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

            <SelectionSection
              title="Current tenant groups"
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
                onClick={() => onSave({
                  displayName: displayName.trim(),
                  email: email.trim(),
                  company: company.trim().length > 0 ? company.trim() : null,
                  isEnabled,
                  accessScope,
                  roles: selectedRoles,
                  teamIds: selectedTeamIds,
                  tenantAccess: tenantAccess.map((item) => ({
                    tenantId: item.tenantId,
                    roles: item.roles,
                  })),
                })}
              >
                {isSaving ? 'Saving access...' : 'Save access model'}
              </Button>
            </div>
          </TabsContent>

          <TabsContent value="audit" className="space-y-4">
            <div className="grid gap-3 md:grid-cols-2">
              <select
                className="rounded-md border border-input bg-background px-3 py-2 text-sm"
                value={auditFilters.entityType}
                onChange={(event) => onAuditFilterChange({ ...auditFilters, entityType: event.target.value })}
              >
                <option value="">All entity types</option>
                <option value="User">User</option>
                <option value="UserTenantRole">Role assignment</option>
                <option value="TeamMember">Group membership</option>
              </select>
              <select
                className="rounded-md border border-input bg-background px-3 py-2 text-sm"
                value={auditFilters.action}
                onChange={(event) => onAuditFilterChange({ ...auditFilters, action: event.target.value })}
              >
                <option value="">All actions</option>
                <option value="Created">Created</option>
                <option value="Updated">Updated</option>
                <option value="Deleted">Deleted</option>
              </select>
            </div>
            <AuditTimeline events={auditEvents} emptyMessage="No audit entries matched the current filters." />
          </TabsContent>
        </Tabs>
      </CardContent>
    </Card>
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
