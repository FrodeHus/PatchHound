import { useState } from 'react'
import type { TeamItem } from '@/api/teams.schemas'
import type { UserAuditItem, UserDetail } from '@/api/users.schemas'
import { AuditTimeline, type AuditTimelineEvent } from '@/components/features/audit/AuditTimeline'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'

const roleOptions = [
  'GlobalAdmin',
  'SecurityManager',
  'SecurityAnalyst',
  'TechnicalManager',
  'AssetOwner',
  'Stakeholder',
  'Auditor',
] as const

type UserDetailPanelProps = {
  user: UserDetail | undefined
  teams: TeamItem[]
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
    roles: string[]
    teamIds: string[]
  }) => void
}

export function UserDetailPanel({
  user,
  teams,
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
          Select a user to review profile, access, assignment groups, and audit history.
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
    user.roles.join(','),
    user.teams.map((team) => team.teamId).join(','),
  ].join('|')

  return (
    <UserDetailEditor
      key={userSnapshotKey}
      user={user}
      teams={teams}
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
  teams,
  auditItems,
  auditFilters,
  isLoading,
  isSaving,
  onAuditFilterChange,
  onSave,
}: {
  user: UserDetail
  teams: TeamItem[]
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
  const [selectedRoles, setSelectedRoles] = useState<string[]>(user.roles)
  const [selectedTeamIds, setSelectedTeamIds] = useState<string[]>(user.teams.map((team) => team.teamId))

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

  return (
    <Card className="rounded-2xl border-border/70">
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle className="text-2xl tracking-[-0.04em]">{user.displayName}</CardTitle>
            <CardDescription className="mt-1">
              Manage tenant access, assignment groups, and the audit trail for this identity.
            </CardDescription>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant="outline" className="rounded-full border-border/70 bg-background/60">
              {user.tenantName}
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

            <div className="rounded-xl border border-border/70 bg-background/50 p-4">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Access status</p>
              <div className="mt-3 flex flex-wrap gap-2">
                <Button
                  type="button"
                  variant={isEnabled ? 'default' : 'outline'}
                  className="rounded-full"
                  onClick={() => setIsEnabled(true)}
                >
                  Enabled
                </Button>
                <Button
                  type="button"
                  variant={!isEnabled ? 'destructive' : 'outline'}
                  className="rounded-full"
                  onClick={() => setIsEnabled(false)}
                >
                  Disabled
                </Button>
              </div>
            </div>

            <SelectionSection
              title="Roles"
              description="Tenant roles granted to this user."
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

            <SelectionSection
              title="Assignment groups"
              description="Membership controls which group-owned workflows and remediation tasks the user can act on."
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
                  roles: selectedRoles,
                  teamIds: selectedTeamIds,
                })}
              >
                {isSaving ? 'Saving...' : 'Save user'}
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
