import type { UserListItem } from '@/api/users.schemas'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { PaginationControls } from '@/components/ui/pagination-controls'

type UserTableProps = {
  users: UserListItem[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  selectedUserId: string | null
  filters: {
    search: string
    role: string
    status: string
    teamId: string
  }
  teams: Array<{ id: string; name: string }>
  onFilterChange: (next: { search: string; role: string; status: string; teamId: string }) => void
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
  onSelectUser: (userId: string) => void
}

export function UserTable({
  users,
  totalCount,
  page,
  pageSize,
  totalPages,
  selectedUserId,
  filters,
  teams,
  onFilterChange,
  onPageChange,
  onPageSizeChange,
  onSelectUser,
}: UserTableProps) {
  return (
    <Card className="rounded-2xl border-border/70">
      <CardHeader className="space-y-4">
        <div>
          <CardTitle>Access directory</CardTitle>
          <CardDescription>
            Review identities, tenant reach, and assignment posture before opening the access editor.
          </CardDescription>
        </div>
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <Input
            placeholder="Search name, email, or company"
            value={filters.search}
            onChange={(event) => onFilterChange({ ...filters, search: event.target.value })}
          />
          <select
            className="rounded-md border border-input bg-background px-3 py-2 text-sm"
            value={filters.role}
            onChange={(event) => onFilterChange({ ...filters, role: event.target.value })}
          >
            <option value="">All roles</option>
            <option value="GlobalAdmin">Global Admin</option>
            <option value="CustomerAdmin">Customer Admin</option>
            <option value="CustomerOperator">Customer Operator</option>
            <option value="CustomerViewer">Customer Viewer</option>
            <option value="SecurityManager">Security Manager</option>
            <option value="SecurityAnalyst">Security Analyst</option>
            <option value="TechnicalManager">Technical Manager</option>
            <option value="AssetOwner">Asset Owner</option>
            <option value="Stakeholder">Stakeholder</option>
            <option value="Auditor">Auditor</option>
          </select>
          <select
            className="rounded-md border border-input bg-background px-3 py-2 text-sm"
            value={filters.status}
            onChange={(event) => onFilterChange({ ...filters, status: event.target.value })}
          >
            <option value="">All statuses</option>
            <option value="enabled">Enabled</option>
            <option value="disabled">Disabled</option>
          </select>
          <select
            className="rounded-md border border-input bg-background px-3 py-2 text-sm"
            value={filters.teamId}
            onChange={(event) => onFilterChange({ ...filters, teamId: event.target.value })}
          >
            <option value="">All assignment groups</option>
            {teams.map((team) => (
              <option key={team.id} value={team.id}>
                {team.name}
              </option>
            ))}
          </select>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
          {totalCount} matching users
        </div>
        {users.length === 0 ? (
          <div className="rounded-xl border border-border/70 bg-background/50 px-4 py-8 text-sm text-muted-foreground">
            No users matched the current filters.
          </div>
        ) : (
          <div className="space-y-2">
            {users.map((user) => {
              const isSelected = user.id === selectedUserId
              return (
                <button
                  key={user.id}
                  type="button"
                  onClick={() => onSelectUser(user.id)}
                  className={`w-full rounded-xl border px-4 py-4 text-left transition ${
                    isSelected
                      ? 'border-primary/40 bg-primary/8'
                      : 'border-border/70 bg-background/40 hover:border-primary/20 hover:bg-background/70'
                  }`}
                >
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <p className="font-medium tracking-tight">{user.displayName}</p>
                        <Badge variant="outline" className="rounded-full border-border/70 bg-background/60">
                          {user.accessScope}
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
                      <p className="mt-1 text-sm text-muted-foreground">{user.email}</p>
                      {user.company ? (
                        <p className="mt-1 text-sm text-muted-foreground">{user.company}</p>
                      ) : null}
                      <div className="mt-2 flex flex-wrap gap-2">
                        {user.roles.map((role) => (
                          <Badge key={role} variant="outline" className="rounded-full border-border/70 bg-background/70">
                            {role}
                          </Badge>
                        ))}
                      </div>
                    </div>
                    <div className="text-right text-sm text-muted-foreground">
                      <div>{user.teams.length} groups</div>
                      <div className="mt-1">
                        {user.tenantNames.length === 0
                          ? 'No tenant access'
                          : user.tenantNames.length === 1
                            ? user.tenantNames[0]
                            : `${user.tenantNames.length} tenants`}
                      </div>
                    </div>
                  </div>
                </button>
              )
            })}
          </div>
        )}
        <PaginationControls
          page={page}
          pageSize={pageSize}
          totalCount={totalCount}
          totalPages={totalPages}
          onPageChange={onPageChange}
          onPageSizeChange={onPageSizeChange}
        />
      </CardContent>
    </Card>
  )
}
