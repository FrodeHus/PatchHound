import { useState } from 'react'
import { SearchIcon } from 'lucide-react'
import type { UserListItem } from '@/api/users.schemas'
import type { TeamItem } from '@/api/teams.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { PaginationControls } from '@/components/ui/pagination-controls'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import {
  DataTableActiveFilters,
  DataTableField,
  DataTableToolbar,
  DataTableToolbarRow,
  DataTableWorkbench,
} from '@/components/ui/data-table-workbench'
import { WorkbenchFilterDrawer, WorkbenchFilterSection } from '@/components/ui/workbench-filter-drawer'
import { cn } from '@/lib/utils'

type UserFilters = {
  search: string
  role: string
  status: string
  teamId: string
}

type UserTableProps = {
  users: UserListItem[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  selectedUserId: string | null
  filters: UserFilters
  teams: Array<{ id: string; name: string }>
  onFilterChange: (next: UserFilters) => void
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
  onSelectUser: (userId: string) => void
}

const roleOptions = [
  { value: 'GlobalAdmin', label: 'Global Admin' },
  { value: 'CustomerAdmin', label: 'Customer Admin' },
  { value: 'CustomerOperator', label: 'Customer Operator' },
  { value: 'CustomerViewer', label: 'Customer Viewer' },
  { value: 'SecurityManager', label: 'Security Manager' },
  { value: 'SecurityAnalyst', label: 'Security Analyst' },
  { value: 'TechnicalManager', label: 'Technical Manager' },
  { value: 'AssetOwner', label: 'Asset Owner' },
  { value: 'Stakeholder', label: 'Stakeholder' },
  { value: 'Auditor', label: 'Auditor' },
]

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
  const [isFilterDrawerOpen, setIsFilterDrawerOpen] = useState(false)
  const [draftFilters, setDraftFilters] = useState<Omit<UserFilters, 'search'>>({
    role: filters.role,
    status: filters.status,
    teamId: filters.teamId,
  })

  const activeStructuredFilterCount = [filters.role, filters.status, filters.teamId].filter(Boolean).length

  const activeFilters = [
    filters.search
      ? { key: 'search', label: `Search: ${filters.search}`, onClear: () => onFilterChange({ ...filters, search: '' }) }
      : null,
    filters.role
      ? {
          key: 'role',
          label: `Role: ${roleOptions.find((r) => r.value === filters.role)?.label ?? filters.role}`,
          onClear: () => onFilterChange({ ...filters, role: '' }),
        }
      : null,
    filters.status
      ? { key: 'status', label: `Status: ${filters.status}`, onClear: () => onFilterChange({ ...filters, status: '' }) }
      : null,
    filters.teamId
      ? {
          key: 'teamId',
          label: `Group: ${teams.find((t) => t.id === filters.teamId)?.name ?? filters.teamId}`,
          onClear: () => onFilterChange({ ...filters, teamId: '' }),
        }
      : null,
  ].filter((f): f is NonNullable<typeof f> => f !== null)

  return (
    <DataTableWorkbench
      title="Access directory"
      description="Select an identity to inspect and update access."
      totalCount={totalCount}
    >
      <DataTableToolbar>
        <DataTableToolbarRow className="items-end gap-4">
          <DataTableField label="Search" className="flex-1">
            <div className="relative">
              <SearchIcon className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                value={filters.search}
                onChange={(event) => onFilterChange({ ...filters, search: event.target.value })}
                placeholder="Search name, email, or company"
                className="h-10 rounded-xl border-border/70 bg-background/80 pl-10"
              />
            </div>
          </DataTableField>
          <Button
            type="button"
            variant="outline"
            className="h-10 rounded-xl border-border/70 bg-background/80 px-4"
            onClick={() => {
              setDraftFilters({ role: filters.role, status: filters.status, teamId: filters.teamId })
              setIsFilterDrawerOpen(true)
            }}
          >
            {activeStructuredFilterCount > 0 ? `Filters (${activeStructuredFilterCount})` : 'Filters...'}
          </Button>
        </DataTableToolbarRow>

        <DataTableToolbarRow>
          <DataTableActiveFilters
            filters={activeFilters}
            onClearAll={() => onFilterChange({ search: '', role: '', status: '', teamId: '' })}
            className="flex-1"
          />
        </DataTableToolbarRow>
      </DataTableToolbar>

      <WorkbenchFilterDrawer
        open={isFilterDrawerOpen}
        onOpenChange={setIsFilterDrawerOpen}
        title="Access filters"
        description="Narrow the identity directory by role, account status, or group membership."
        activeCount={activeStructuredFilterCount}
        onResetDraft={() => setDraftFilters({ role: '', status: '', teamId: '' })}
        onApply={() => {
          onFilterChange({ ...filters, ...draftFilters })
          setIsFilterDrawerOpen(false)
        }}
      >
        <WorkbenchFilterSection
          title="Identity"
          description="Filter by the role this user holds in the current tenant."
        >
          <DataTableField label="Role">
            <Select
              value={draftFilters.role || 'all'}
              onValueChange={(value) =>
                setDraftFilters((current) => ({ ...current, role: value === 'all' ? '' : (value ?? '') }))
              }
            >
              <SelectTrigger className="h-10 w-full rounded-xl border-border/70 bg-background/80 px-3">
                <SelectValue placeholder="Any role" />
              </SelectTrigger>
              <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                <SelectItem value="all">Any role</SelectItem>
                {roleOptions.map((option) => (
                  <SelectItem key={option.value} value={option.value}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </DataTableField>
        </WorkbenchFilterSection>

        <WorkbenchFilterSection
          title="Account"
          description="Show only enabled or disabled accounts."
        >
          <DataTableField label="Status">
            <Select
              value={draftFilters.status || 'all'}
              onValueChange={(value) =>
                setDraftFilters((current) => ({ ...current, status: value === 'all' ? '' : (value ?? '') }))
              }
            >
              <SelectTrigger className="h-10 w-full rounded-xl border-border/70 bg-background/80 px-3">
                <SelectValue placeholder="Any status" />
              </SelectTrigger>
              <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                <SelectItem value="all">Any status</SelectItem>
                <SelectItem value="enabled">Enabled</SelectItem>
                <SelectItem value="disabled">Disabled</SelectItem>
              </SelectContent>
            </Select>
          </DataTableField>
        </WorkbenchFilterSection>

        <WorkbenchFilterSection
          title="Membership"
          description="Filter to users who belong to a specific assignment group."
        >
          <DataTableField label="Assignment group">
            <Select
              value={draftFilters.teamId || 'all'}
              onValueChange={(value) =>
                setDraftFilters((current) => ({ ...current, teamId: value === 'all' ? '' : (value ?? '') }))
              }
            >
              <SelectTrigger className="h-10 w-full rounded-xl border-border/70 bg-background/80 px-3">
                <SelectValue placeholder="Any group" />
              </SelectTrigger>
              <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                <SelectItem value="all">Any group</SelectItem>
                {teams.map((team) => (
                  <SelectItem key={team.id} value={team.id}>
                    {team.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </DataTableField>
        </WorkbenchFilterSection>
      </WorkbenchFilterDrawer>

      {users.length === 0 ? (
        <div className="rounded-xl border border-border/70 bg-background/50 px-4 py-10 text-center text-sm text-muted-foreground">
          No users matched the current filters.
        </div>
      ) : (
        <div className="overflow-hidden rounded-xl border border-border/70 bg-background/40">
          <Table aria-label="Access directory">
            <TableHeader>
              <TableRow className="bg-muted/45 hover:bg-muted/45">
                <TableHead className="px-3 py-2 text-xs uppercase tracking-[0.14em] text-muted-foreground">Display name</TableHead>
                <TableHead className="px-3 py-2 text-xs uppercase tracking-[0.14em] text-muted-foreground">Email / UPN</TableHead>
                <TableHead className="px-3 py-2 text-xs uppercase tracking-[0.14em] text-muted-foreground">Scope</TableHead>
                <TableHead className="px-3 py-2 text-xs uppercase tracking-[0.14em] text-muted-foreground">Status</TableHead>
                <TableHead className="px-3 py-2 text-xs uppercase tracking-[0.14em] text-muted-foreground">Tenant access</TableHead>
                <TableHead className="px-3 py-2 text-xs uppercase tracking-[0.14em] text-muted-foreground">Roles</TableHead>
                <TableHead className="px-3 py-2 text-xs uppercase tracking-[0.14em] text-muted-foreground">Groups</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {users.map((user) => {
                const isSelected = user.id === selectedUserId
                const tenantSummary =
                  user.tenantNames.length === 0
                    ? 'No tenant access'
                    : user.tenantNames.length === 1
                      ? user.tenantNames[0]
                      : `${user.tenantNames.length} tenants`

                return (
                  <TableRow
                    key={user.id}
                    data-state={isSelected ? 'selected' : undefined}
                    tabIndex={0}
                    className={cn(
                      'cursor-pointer border-border/60',
                      isSelected && 'bg-primary/8 hover:bg-primary/10',
                    )}
                    onClick={() => onSelectUser(user.id)}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault()
                        onSelectUser(user.id)
                      }
                    }}
                  >
                    <TableCell className="max-w-[14rem] px-3 py-3">
                      <div className="min-w-0">
                        <p className="truncate font-medium tracking-tight">{user.displayName}</p>
                        {user.company ? (
                          <p className="mt-0.5 truncate text-xs text-muted-foreground">{user.company}</p>
                        ) : null}
                      </div>
                    </TableCell>
                    <TableCell className="max-w-[16rem] px-3 py-3 text-muted-foreground">
                      <span className="block truncate">{user.email}</span>
                    </TableCell>
                    <TableCell className="px-3 py-3">
                      <Badge variant="outline" className="rounded-full border-border/70 bg-background/70">
                        {user.accessScope}
                      </Badge>
                    </TableCell>
                    <TableCell className="px-3 py-3">
                      <Badge
                        variant="outline"
                        className={
                          user.isEnabled
                            ? 'rounded-full border-emerald-300/60 bg-emerald-500/10 text-emerald-700'
                            : 'rounded-full border-rose-300/60 bg-rose-500/10 text-rose-700'
                        }
                      >
                        {user.isEnabled ? 'Enabled' : 'Disabled'}
                      </Badge>
                    </TableCell>
                    <TableCell className="px-3 py-3 text-muted-foreground">{tenantSummary}</TableCell>
                    <TableCell className="max-w-[13rem] px-3 py-3">
                      <span className="block truncate text-muted-foreground">
                        {user.roles.length > 0 ? user.roles.join(', ') : 'No roles'}
                      </span>
                    </TableCell>
                    <TableCell className="px-3 py-3 text-muted-foreground">{user.teams.length} groups</TableCell>
                  </TableRow>
                )
              })}
            </TableBody>
          </Table>
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
    </DataTableWorkbench>
  )
}
